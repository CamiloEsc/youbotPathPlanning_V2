"""
Pipeline de analisis para responder a los revisores del paper
"Segment-Level Cost Decomposition of a Grid-Based Navigation Stack on an
Omnidirectional Mobile Base".

Lee uno o mas segment_summary.csv generados por VRepClient (uno por carpeta
ExperimentData/<timestamp>/), filtra por configuracion (circuito, modo de
control, heuristica del planificador, tolerancia de llegada) y produce:

  1. Tabla 1 (nueva): geometria real del circuito activo, con distancia
     ejecutada y guiada real (no solo la proxy de linea recta).
  2. Tabla 2 (nueva): modelo aditivo T = alpha + beta*d + gamma*|dpsi|,
     ajustado igual que en el paper (OLS sobre N observaciones para los
     puntos, OLS sobre las 10 medias por segmento para EE/IC con 7 gl).
  3. Prueba de falta de ajuste (pure error vs lack of fit) si hay >=2
     repeticiones por segmento.
  4. Validacion instrumentada (punto #1 de los revisores): compara lo que
     el modelo atribuye a traslacion/rotacion/overhead contra los tiempos
     de fase medidos directamente (TimeTranslating/TimeRotating/
     TimeConverging+TimeReplanning).
  5. Cuantificacion del error de las proxies (punto #2): distancia recta
     vs distancia realmente recorrida, y cambio de rumbo proxy vs yaw
     integrado real.
  6. Covariable de holgura (clearance, punto #3): correlacion con los
     residuos y modelo extendido con holgura como regresor adicional.
  7. Figuras equivalentes a las Figuras 2 y 3 del paper, mas las nuevas
     comparaciones de arriba.

Uso:
    python analyze_experiment.py <ExperimentData_root> --out <carpeta_salida>
        [--circuit NOMBRE] [--control-mode NOMBRE] [--planner NOMBRE]
        [--tolerance VALOR] [--label ETIQUETA]

Si no hay carpetas que hagan match, no fabrica nada: informa y termina.
"""

import argparse
import glob
import os
import sys

import numpy as np
import pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from scipy import stats
import statsmodels.api as sm
import statsmodels.formula.api as smf


def load_segment_summaries(root):
    """Concatena todos los segment_summary.csv bajo root, dando a cada
    corrida un RunID globalmente unico (RunID solo es unico DENTRO de una
    misma carpeta con marca de tiempo)."""
    paths = sorted(glob.glob(os.path.join(root, "**", "segment_summary.csv"), recursive=True))
    if not paths:
        print(f"No se encontro ningun segment_summary.csv bajo {root}", file=sys.stderr)
        sys.exit(1)

    frames = []
    for p in paths:
        session = os.path.basename(os.path.dirname(p))
        df = pd.read_csv(p)
        if df.empty:
            # Una sesion que fallo antes de completar el primer segmento deja
            # un segment_summary.csv con solo encabezado (0 filas). Sus
            # columnas numericas quedan dtype "object" al no tener datos para
            # inferir tipo, lo que degrada TODO el concat a texto si se incluye.
            continue
        df["Session"] = session
        df["GlobalRunID"] = session + "_" + df["RunID"].astype(str)
        df["SourceFile"] = p
        frames.append(df)
    all_df = pd.concat(frames, ignore_index=True)
    print(f"Cargados {len(paths)} archivo(s) segment_summary.csv, {len(all_df)} filas totales.")
    return all_df


def apply_filters(df, args):
    out = df[df["Outcome"] == "Completed"].copy()
    dropped = len(df) - len(out)
    if dropped > 0:
        print(f"Se excluyeron {dropped} fila(s) con Outcome != Completed (colisiones/abortos).")

    # Bug ya corregido en Form1.cs (CurrentMission no se reseteaba entre
    # repeticiones): las corridas capturadas ANTES del fix tienen una fila S0
    # espuria por cada repeticion que no es la primera, con los datos
    # duplicados del ultimo segmento de la repeticion anterior. Se descarta.
    n_s0 = (out["SegmentIndex"] == "S0").sum()
    if n_s0 > 0:
        print(f"Se excluyeron {n_s0} fila(s) 'S0' espurias (bug de logging ya corregido).")
    out = out[out["SegmentIndex"] != "S0"]

    if args.circuit:
        out = out[out["CircuitName"] == args.circuit]
    if args.control_mode:
        out = out[out["ControlMode"] == args.control_mode]
    if args.planner:
        out = out[out["PlannerHeuristic"] == args.planner]
    if args.tolerance is not None:
        out = out[np.isclose(out["ArrivalTolerance"], args.tolerance)]

    if out.empty:
        print("Ningun registro sobrevive a los filtros pedidos.", file=sys.stderr)
        sys.exit(1)

    # Una corrida que se corto a mitad de camino (colision, PATH_NOT_FOUND
    # persistente, detencion manual) deja menos segmentos "Completed" que el
    # resto. Mezclarla en el ajuste desbalancea las replicas por segmento
    # (justo lo que el lack-of-fit test necesita que sea parejo), asi que se
    # excluye del analisis principal y se informa por separado.
    n_segments_expected = out["SegmentIndex"].nunique()
    seg_counts = out.groupby("GlobalRunID")["SegmentIndex"].nunique()
    incomplete_runs = seg_counts[seg_counts < n_segments_expected].index.tolist()
    if incomplete_runs:
        print(f"Corrida(s) incompleta(s) excluida(s) del ajuste principal (no llegaron a los "
              f"{n_segments_expected} segmentos, probablemente por colision/falla): {incomplete_runs}")
        out = out[~out["GlobalRunID"].isin(incomplete_runs)]

    if out.empty:
        print("No queda ninguna corrida COMPLETA tras excluir las incompletas.", file=sys.stderr)
        sys.exit(1)

    n_segments = out["SegmentIndex"].nunique()
    n_runs = out["GlobalRunID"].nunique()
    print(f"Subconjunto analizado: {len(out)} filas, {n_segments} segmentos distintos, {n_runs} corrida(s) completa(s).")
    return out


def segment_order_key(seg):
    return int(seg[1:]) if seg.startswith("S") and seg[1:].isdigit() else seg


def build_segment_table(df):
    """Tabla 1 equivalente: geometria real (media +/- DE) por segmento."""
    agg = df.groupby("SegmentIndex").agg(
        n=("TotalSegmentTime", "count"),
        StraightLineDistance=("StraightLineDistance", "mean"),
        PlannedPathLengthM=("PlannedPathLengthM", "mean"),
        ExecutedPathLength_mean=("ExecutedPathLength", "mean"),
        ExecutedPathLength_sd=("ExecutedPathLength", "std"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        IntegratedAbsYawDeg_mean=("IntegratedAbsYawDeg", "mean"),
        IntegratedAbsYawDeg_sd=("IntegratedAbsYawDeg", "std"),
        MinClearanceRadius=("MinClearanceRadius", "mean"),
        TotalSegmentTime_mean=("TotalSegmentTime", "mean"),
        TotalSegmentTime_sd=("TotalSegmentTime", "std"),
        TimeRotating_mean=("TimeRotating", "mean"),
        TimeTranslating_mean=("TimeTranslating", "mean"),
        TimeConverging_mean=("TimeConverging", "mean"),
        TimeReplanning_mean=("TimeReplanning", "mean"),
    ).reset_index()
    agg = agg.sort_values(by="SegmentIndex", key=lambda s: s.map(segment_order_key))
    return agg


def fit_additive_model(df):
    """Ajusta T = alpha + beta*d + gamma*|dpsi|, replicando el enfoque del
    paper: estimadores puntuales sobre las N observaciones crudas, EE/IC
    sobre las 10 medias por segmento (grados de libertad = n_segmentos-3)."""
    raw = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=df).fit()

    means = df.groupby("SegmentIndex").agg(
        TotalSegmentTime=("TotalSegmentTime", "mean"),
        StraightLineDistance=("StraightLineDistance", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
    ).reset_index()
    n_seg = len(means)
    means_fit = None
    if n_seg >= 4:
        means_fit = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=means).fit()

    return raw, means_fit, means


def lack_of_fit_test(df, raw_fit):
    """Particiona la SS residual del ajuste crudo en pure error (dentro de
    cada segmento, entre repeticiones) y falta de ajuste (entre las medias
    de los 10 segmentos y el modelo). Requiere >=2 repeticiones en al menos
    un segmento para tener grados de libertad de pure error > 0."""
    reps_per_segment = df.groupby("SegmentIndex")["TotalSegmentTime"].count()
    if (reps_per_segment < 2).all():
        return None

    n_segments = df["SegmentIndex"].nunique()
    n_total = len(df)
    df_pure_error = n_total - n_segments
    if df_pure_error <= 0:
        return None

    grand_ss_resid = raw_fit.ssr
    pure_error_ss = 0.0
    for seg, group in df.groupby("SegmentIndex"):
        pure_error_ss += ((group["TotalSegmentTime"] - group["TotalSegmentTime"].mean()) ** 2).sum()

    lof_ss = grand_ss_resid - pure_error_ss
    df_lof = n_segments - 3
    if df_lof <= 0 or df_pure_error <= 0:
        return None

    ms_lof = lof_ss / df_lof
    ms_pe = pure_error_ss / df_pure_error
    f_stat = ms_lof / ms_pe if ms_pe > 0 else np.nan
    p_value = 1 - stats.f.cdf(f_stat, df_lof, df_pure_error) if np.isfinite(f_stat) else np.nan

    return {
        "pure_error_ss": pure_error_ss,
        "df_pure_error": df_pure_error,
        "pure_error_sd": np.sqrt(ms_pe),
        "lof_ss": lof_ss,
        "df_lof": df_lof,
        "f_stat": f_stat,
        "p_value": p_value,
        "pct_systematic": 100 * lof_ss / grand_ss_resid if grand_ss_resid > 0 else np.nan,
    }


def run_clustering_test(df):
    """ANOVA de los residuos agrupados por corrida (execution), igual que
    el chequeo de exchangeability del paper. Requiere >=2 corridas."""
    n_runs = df["GlobalRunID"].nunique()
    if n_runs < 2:
        return None
    raw_fit = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=df).fit()
    df = df.copy()
    df["resid"] = raw_fit.resid
    groups = [g["resid"].values for _, g in df.groupby("GlobalRunID")]
    f_stat, p_value = stats.f_oneway(*groups)
    run_means = df.groupby("GlobalRunID")["TotalSegmentTime"].sum()
    return {
        "f_stat": f_stat,
        "p_value": p_value,
        "df_between": n_runs - 1,
        "df_within": len(df) - n_runs,
        "mission_mean": run_means.mean(),
        "mission_sd": run_means.std(),
        "mission_cv_pct": 100 * run_means.std() / run_means.mean() if run_means.mean() else np.nan,
    }


def attribution_summary(df, raw_fit):
    """Aplica los coeficientes a la geometria total del circuito, igual
    que la Seccion 3.2 del paper."""
    total_d = df.groupby("SegmentIndex")["StraightLineDistance"].mean().sum()
    total_dpsi = df.groupby("SegmentIndex")["HeadingProxyDeg"].mean().sum()
    n_segments = df["SegmentIndex"].nunique()

    alpha = raw_fit.params["Intercept"]
    beta = raw_fit.params["StraightLineDistance"]
    gamma = raw_fit.params["HeadingProxyDeg"]

    fixed_total = alpha * n_segments
    translation_total = beta * total_d
    rotation_total = gamma * total_dpsi
    predicted_mission = fixed_total + translation_total + rotation_total

    mission_means = df.groupby("GlobalRunID")["TotalSegmentTime"].sum()
    measured_mission = mission_means.mean()

    return {
        "alpha": alpha, "beta": beta, "gamma": gamma,
        "n_segments": n_segments, "total_d": total_d, "total_dpsi": total_dpsi,
        "fixed_total": fixed_total, "translation_total": translation_total,
        "rotation_total": rotation_total, "predicted_mission": predicted_mission,
        "measured_mission": measured_mission,
        "pct_fixed": 100 * fixed_total / predicted_mission,
        "pct_translation": 100 * translation_total / predicted_mission,
        "pct_rotation": 100 * rotation_total / predicted_mission,
    }


def instrumented_validation(df, raw_fit):
    """Punto #1 de los revisores: compara lo que el modelo ATRIBUYE a cada
    termino (beta*d_i para traslacion, gamma*|dpsi|_i para reorientacion,
    alpha para overhead fijo) contra los tiempos de fase MEDIDOS
    directamente por segmento (promediados entre repeticiones)."""
    alpha = raw_fit.params["Intercept"]
    beta = raw_fit.params["StraightLineDistance"]
    gamma = raw_fit.params["HeadingProxyDeg"]

    seg = df.groupby("SegmentIndex").agg(
        StraightLineDistance=("StraightLineDistance", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        TimeTranslating=("TimeTranslating", "mean"),
        TimeRotating=("TimeRotating", "mean"),
        TimeConverging=("TimeConverging", "mean"),
        TimeReplanning=("TimeReplanning", "mean"),
    ).reset_index()
    seg["ModelTranslation"] = beta * seg["StraightLineDistance"]
    seg["ModelRotation"] = gamma * seg["HeadingProxyDeg"]
    seg["ModelFixedOverhead"] = alpha
    seg["MeasuredOverhead"] = seg["TimeConverging"] + seg["TimeReplanning"]

    r_translation = np.corrcoef(seg["ModelTranslation"], seg["TimeTranslating"])[0, 1] if len(seg) > 2 else np.nan
    r_rotation = np.corrcoef(seg["ModelRotation"], seg["TimeRotating"])[0, 1] if len(seg) > 2 else np.nan

    return seg, r_translation, r_rotation


def proxy_error_summary(df):
    """Punto #2 de los revisores: error de las proxies respecto a las
    cantidades fisicas reales."""
    seg = df.groupby("SegmentIndex").agg(
        StraightLineDistance=("StraightLineDistance", "mean"),
        ExecutedPathLength=("ExecutedPathLength", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        IntegratedAbsYawDeg=("IntegratedAbsYawDeg", "mean"),
    ).reset_index()
    seg["DistanceRatio"] = seg["ExecutedPathLength"] / seg["StraightLineDistance"]
    seg["YawRatio"] = seg["IntegratedAbsYawDeg"] / seg["HeadingProxyDeg"].replace(0, np.nan)
    return seg


def clearance_covariate_test(df):
    """Punto #3 de los revisores: prueba un modelo extendido con la
    holgura minima (MinClearanceRadius) como regresor adicional."""
    means = df.groupby("SegmentIndex").agg(
        TotalSegmentTime=("TotalSegmentTime", "mean"),
        StraightLineDistance=("StraightLineDistance", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        MinClearanceRadius=("MinClearanceRadius", "mean"),
    ).reset_index()
    if len(means) < 5 or means["MinClearanceRadius"].nunique() < 2:
        return None, means
    base = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=means).fit()
    extended = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg + MinClearanceRadius", data=means).fit()
    return {"base_r2": base.rsquared, "extended_r2": extended.rsquared,
            "clearance_coef": extended.params.get("MinClearanceRadius", np.nan),
            "clearance_p": extended.pvalues.get("MinClearanceRadius", np.nan)}, means


def make_figures(df, raw_fit, means_fit, means, seg_instrumented, out_dir, label):
    os.makedirs(out_dir, exist_ok=True)
    df = df.copy()
    df["Predicted"] = raw_fit.predict(df)

    # Fig 2a: observado vs predicho
    fig, ax = plt.subplots(figsize=(5, 5))
    ax.scatter(df["Predicted"], df["TotalSegmentTime"], alpha=0.6)
    lims = [min(df["Predicted"].min(), df["TotalSegmentTime"].min()),
            max(df["Predicted"].max(), df["TotalSegmentTime"].max())]
    ax.plot(lims, lims, "k--", linewidth=1)
    ax.set_xlabel("Duracion predicha (s)")
    ax.set_ylabel("Duracion observada (s)")
    ax.set_title(f"Ajuste del modelo aditivo — {label}")
    fig.tight_layout()
    fig.savefig(os.path.join(out_dir, "fig2a_observado_vs_predicho.png"), dpi=150)
    plt.close(fig)

    # Fig 2c: decomposicion medida por segmento (apilada), la version
    # instrumentada real (no solo lo que el modelo infiere).
    order = seg_instrumented.sort_values(by="SegmentIndex", key=lambda s: s.map(segment_order_key))
    fig, ax = plt.subplots(figsize=(8, 5))
    x = np.arange(len(order))
    ax.bar(x, order["TimeTranslating"], label="Traslacion (medida)")
    ax.bar(x, order["TimeRotating"], bottom=order["TimeTranslating"], label="Rotacion (medida)")
    bottom2 = order["TimeTranslating"] + order["TimeRotating"]
    ax.bar(x, order["TimeConverging"], bottom=bottom2, label="Convergencia final (medida)")
    bottom3 = bottom2 + order["TimeConverging"]
    ax.bar(x, order["TimeReplanning"], bottom=bottom3, label="Replanificacion (medida)")
    ax.set_xticks(x)
    ax.set_xticklabels(order["SegmentIndex"])
    ax.set_ylabel("Tiempo (s)")
    ax.set_title(f"Descomposicion INSTRUMENTADA por segmento — {label}")
    ax.legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(os.path.join(out_dir, "fig2c_descomposicion_medida.png"), dpi=150)
    plt.close(fig)

    # Fig proxy error: distancia recta vs ejecutada, proxy de rumbo vs yaw integrado
    proxy = proxy_error_summary(df)
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.5))
    axes[0].scatter(proxy["StraightLineDistance"], proxy["ExecutedPathLength"])
    lims0 = [0, max(proxy["StraightLineDistance"].max(), proxy["ExecutedPathLength"].max()) * 1.05]
    axes[0].plot(lims0, lims0, "k--", linewidth=1)
    axes[0].set_xlabel("Distancia en linea recta (m)")
    axes[0].set_ylabel("Distancia realmente ejecutada (m)")
    axes[0].set_title("Proxy de distancia vs real")

    axes[1].scatter(proxy["HeadingProxyDeg"], proxy["IntegratedAbsYawDeg"])
    lims1 = [0, max(proxy["HeadingProxyDeg"].max(), proxy["IntegratedAbsYawDeg"].max()) * 1.05]
    axes[1].plot(lims1, lims1, "k--", linewidth=1)
    axes[1].set_xlabel("Proxy |dpsi| entre segmentos (deg)")
    axes[1].set_ylabel("Yaw integrado real (deg)")
    axes[1].set_title("Proxy de rumbo vs real")
    fig.suptitle(f"Cuantificacion del error de las proxies — {label}")
    fig.tight_layout()
    fig.savefig(os.path.join(out_dir, "fig_proxy_vs_real.png"), dpi=150)
    plt.close(fig)

    # Fig 3: residual diagnostico
    resid_by_seg = df.groupby("SegmentIndex").apply(
        lambda g: (g["TotalSegmentTime"] - g["Predicted"]).mean(), include_groups=False
    ).reset_index(name="Residual")
    resid_by_seg = resid_by_seg.sort_values(by="SegmentIndex", key=lambda s: s.map(segment_order_key))
    fig, ax = plt.subplots(figsize=(7, 4))
    ax.axhline(0, color="k", linewidth=1)
    ax.bar(resid_by_seg["SegmentIndex"], resid_by_seg["Residual"])
    ax.set_ylabel("Observado - predicho (s)")
    ax.set_title(f"Diagnostico de residuos por segmento — {label}")
    fig.tight_layout()
    fig.savefig(os.path.join(out_dir, "fig3_residuos.png"), dpi=150)
    plt.close(fig)

    print(f"Figuras guardadas en: {out_dir}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("root", help="Carpeta raiz con subcarpetas ExperimentData/<timestamp>/")
    p.add_argument("--out", required=True, help="Carpeta de salida para tablas y figuras")
    p.add_argument("--circuit", default=None)
    p.add_argument("--control-mode", default=None)
    p.add_argument("--planner", default=None)
    p.add_argument("--tolerance", type=float, default=None)
    p.add_argument("--label", default="baseline")
    args = p.parse_args()

    os.makedirs(args.out, exist_ok=True)

    all_df = load_segment_summaries(args.root)
    df = apply_filters(all_df, args)

    seg_table = build_segment_table(df)
    seg_table.to_csv(os.path.join(args.out, "tabla1_geometria_segmentos.csv"), index=False)
    print("\n=== TABLA 1 (geometria real por segmento) ===")
    print(seg_table.to_string(index=False))

    raw_fit, means_fit, means = fit_additive_model(df)
    print("\n=== TABLA 2 (modelo aditivo, estimadores puntuales sobre N crudo) ===")
    print(raw_fit.summary())

    if means_fit is not None:
        print("\n=== TABLA 2 (EE/IC sobre medias por segmento, gl=%d) ===" % means_fit.df_resid)
        print(means_fit.summary())
    else:
        print("\n(No hay suficientes segmentos distintos para el ajuste sobre medias)")

    lof = lack_of_fit_test(df, raw_fit)
    print("\n=== PRUEBA DE FALTA DE AJUSTE ===")
    if lof is None:
        print("Se necesitan >=2 repeticiones por segmento para separar pure error de falta de ajuste. "
              "Con los datos actuales no se puede calcular todavia.")
    else:
        print(f"Pure error SD = {lof['pure_error_sd']:.3f} s (df={lof['df_pure_error']})")
        print(f"Falta de ajuste F({lof['df_lof']},{lof['df_pure_error']}) = {lof['f_stat']:.3f}, p = {lof['p_value']:.2e}")
        print(f"% de la SS residual que es sistematico: {lof['pct_systematic']:.1f}%")

    clustering = run_clustering_test(df)
    print("\n=== CLUSTERING ENTRE CORRIDAS (exchangeability) ===")
    if clustering is None:
        print("Se necesita mas de una corrida (repeticion) para este chequeo.")
    else:
        print(f"F({clustering['df_between']},{clustering['df_within']}) = {clustering['f_stat']:.3f}, p = {clustering['p_value']:.3f}")
        print(f"Duracion de mision: media={clustering['mission_mean']:.1f} s, "
              f"DE={clustering['mission_sd']:.2f} s, CV={clustering['mission_cv_pct']:.2f}%")

    attrib = attribution_summary(df, raw_fit)
    print("\n=== ATRIBUCION (aplicando coeficientes a la geometria del circuito) ===")
    print(f"alpha={attrib['alpha']:.3f} s, beta={attrib['beta']:.4f} s/m, gamma={attrib['gamma']:.5f} s/deg")
    print(f"Mision predicha: {attrib['predicted_mission']:.1f} s | Mision medida (media): {attrib['measured_mission']:.1f} s")
    print(f"Traslacion: {attrib['translation_total']:.1f} s ({attrib['pct_translation']:.1f}%)")
    print(f"Overhead fijo: {attrib['fixed_total']:.1f} s ({attrib['pct_fixed']:.1f}%)")
    print(f"Reorientacion: {attrib['rotation_total']:.1f} s ({attrib['pct_rotation']:.1f}%)")

    seg_instr, r_trans, r_rot = instrumented_validation(df, raw_fit)
    seg_instr.to_csv(os.path.join(args.out, "validacion_instrumentada.csv"), index=False)
    print("\n=== VALIDACION INSTRUMENTADA (punto #1 revisores) ===")
    print(seg_instr.to_string(index=False))
    print(f"Correlacion atribucion-traslacion (modelo) vs TimeTranslating (medido): r={r_trans:.3f}" if np.isfinite(r_trans) else "r no calculable (pocos segmentos)")
    print(f"Correlacion atribucion-rotacion (modelo) vs TimeRotating (medido): r={r_rot:.3f}" if np.isfinite(r_rot) else "r no calculable (pocos segmentos)")

    proxy = proxy_error_summary(df)
    proxy.to_csv(os.path.join(args.out, "error_proxies.csv"), index=False)
    print("\n=== ERROR DE LAS PROXIES (punto #2 revisores) ===")
    print(proxy.to_string(index=False))
    print(f"Ratio distancia ejecutada/recta: media={proxy['DistanceRatio'].mean():.3f}, max={proxy['DistanceRatio'].max():.3f}")
    print(f"Ratio yaw integrado/proxy: media={proxy['YawRatio'].mean():.3f}, max={proxy['YawRatio'].max():.3f}")

    clear_test, clear_means = clearance_covariate_test(df)
    print("\n=== COVARIABLE DE HOLGURA / CLEARANCE (punto #3 revisores) ===")
    if clear_test is None:
        print("Holgura casi constante o muy pocos segmentos distintos; no se puede probar el modelo extendido todavia "
              "(hace falta un circuito con clearance mas variado, punto #8).")
    else:
        print(f"R2 base = {clear_test['base_r2']:.4f}, R2 con holgura = {clear_test['extended_r2']:.4f}")
        print(f"Coeficiente de holgura = {clear_test['clearance_coef']:.4f} (p={clear_test['clearance_p']:.3f})")

    make_figures(df, raw_fit, means_fit, means, seg_instr, args.out, args.label)

    print(f"\nTodo listo. Tablas y figuras en: {args.out}")


if __name__ == "__main__":
    main()
