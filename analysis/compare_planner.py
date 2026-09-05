"""
Compara la heuristica admisible (Octile) contra la linea base (Manhattan),
mismo controlador y circuito, para el punto #6 de los revisores.

Uso:
    python compare_planner.py <baseline_csv> <octile_root> --out <carpeta>

baseline_csv: el dataset_segmentos_circuito_base.csv (esquema final, Manhattan).
octile_root: carpeta con segment_summary.csv del experimento Octile.
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
import statsmodels.formula.api as smf


def load_baseline(path):
    df = pd.read_csv(path)
    df = df.rename(columns={
        "StraightLineDistance_m": "StraightLineDistance",
        "HeadingProxy_deg": "HeadingProxyDeg",
        "ExecutedPathLength_m": "ExecutedPathLength",
        "PlannedPathLength_m": "PlannedPathLengthM",
        "TotalSegmentTime_s": "TotalSegmentTime",
    })
    df["Exec"] = df["Execution"]
    df["Label"] = "Manhattan"
    return df


def load_octile(root):
    paths = glob.glob(os.path.join(root, "**", "segment_summary.csv"), recursive=True)
    frames = []
    for p in paths:
        d = pd.read_csv(p)
        frames.append(d)
    df = pd.concat(frames, ignore_index=True)
    df = df[(df["Outcome"] == "Completed") & (df["SegmentIndex"] != "S0")]
    df["Exec"] = df["RunID"]
    df["Label"] = "Octile"
    return df


def summarize(df, label):
    per_exec = df.groupby("Exec").agg(
        Mission_s=("TotalSegmentTime", "sum"),
        Planned_m=("PlannedPathLengthM", "sum"),
        Executed_m=("ExecutedPathLength", "sum"),
    )
    straight_total = df.groupby("Exec")["StraightLineDistance"].sum().iloc[0]
    fit = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=df).fit()
    print(f"\n=== {label} (N={per_exec.shape[0]} ejecuciones) ===")
    print(f"Distancia total en linea recta del circuito: {straight_total:.2f} m")
    print(f"Longitud PLANIFICADA total (media por ejecucion): {per_exec['Planned_m'].mean():.2f} m "
          f"(ratio vs recta: {per_exec['Planned_m'].mean()/straight_total:.3f}x)")
    print(f"Longitud EJECUTADA total (media por ejecucion): {per_exec['Executed_m'].mean():.2f} m "
          f"(ratio vs recta: {per_exec['Executed_m'].mean()/straight_total:.3f}x)")
    print(f"Duracion de mision: media={per_exec['Mission_s'].mean():.1f} s, DE={per_exec['Mission_s'].std():.2f} s")
    print(f"Modelo: alpha={fit.params['Intercept']:.3f} s, beta={fit.params['StraightLineDistance']:.4f} s/m, "
          f"gamma={fit.params['HeadingProxyDeg']:.5f} s/deg, R2={fit.rsquared:.4f}")
    return per_exec, straight_total, fit


def main():
    p = argparse.ArgumentParser()
    p.add_argument("baseline_csv")
    p.add_argument("octile_root")
    p.add_argument("--out", required=True)
    args = p.parse_args()
    os.makedirs(args.out, exist_ok=True)

    base = load_baseline(args.baseline_csv)
    oct_ = load_octile(args.octile_root)

    base_exec, base_straight, base_fit = summarize(base, "Manhattan (linea base)")
    oct_exec, oct_straight, oct_fit = summarize(oct_, "Octile (admisible)")

    # Prueba t de Welch para duracion de mision y longitud planificada/ejecutada
    from scipy import stats as sstats
    t_mission = sstats.ttest_ind(base_exec["Mission_s"], oct_exec["Mission_s"], equal_var=False)
    t_planned = sstats.ttest_ind(base_exec["Planned_m"], oct_exec["Planned_m"], equal_var=False)
    t_executed = sstats.ttest_ind(base_exec["Executed_m"], oct_exec["Executed_m"], equal_var=False)

    print("\n=== COMPARACION DIRECTA (mismo controlador y circuito) ===")
    print(f"Duracion de mision: Manhattan {base_exec['Mission_s'].mean():.1f}s vs Octile {oct_exec['Mission_s'].mean():.1f}s "
          f"(diferencia {oct_exec['Mission_s'].mean()-base_exec['Mission_s'].mean():+.1f}s, t={t_mission.statistic:.2f}, p={t_mission.pvalue:.3f})")
    print(f"Longitud planificada: Manhattan {base_exec['Planned_m'].mean():.2f}m vs Octile {oct_exec['Planned_m'].mean():.2f}m "
          f"(t={t_planned.statistic:.2f}, p={t_planned.pvalue:.3f})")
    print(f"Longitud ejecutada: Manhattan {base_exec['Executed_m'].mean():.2f}m vs Octile {oct_exec['Executed_m'].mean():.2f}m "
          f"(t={t_executed.statistic:.2f}, p={t_executed.pvalue:.3f})")

    # Tabla resumen
    summary = pd.DataFrame({
        "Metric": ["N_executions", "StraightLineTotal_m", "PlannedPathTotal_m_mean", "PlannedPathTotal_m_sd",
                   "ExecutedPathTotal_m_mean", "ExecutedPathTotal_m_sd", "MissionDuration_s_mean", "MissionDuration_s_sd",
                   "alpha_s", "beta_s_per_m", "gamma_s_per_deg", "R2_raw"],
        "Manhattan": [base_exec.shape[0], base_straight, base_exec["Planned_m"].mean(), base_exec["Planned_m"].std(),
                      base_exec["Executed_m"].mean(), base_exec["Executed_m"].std(), base_exec["Mission_s"].mean(), base_exec["Mission_s"].std(),
                      base_fit.params["Intercept"], base_fit.params["StraightLineDistance"], base_fit.params["HeadingProxyDeg"], base_fit.rsquared],
        "Octile": [oct_exec.shape[0], oct_straight, oct_exec["Planned_m"].mean(), oct_exec["Planned_m"].std(),
                   oct_exec["Executed_m"].mean(), oct_exec["Executed_m"].std(), oct_exec["Mission_s"].mean(), oct_exec["Mission_s"].std(),
                   oct_fit.params["Intercept"], oct_fit.params["StraightLineDistance"], oct_fit.params["HeadingProxyDeg"], oct_fit.rsquared],
    })
    summary.to_csv(os.path.join(args.out, "comparacion_planificador.csv"), index=False)
    print(f"\nTabla guardada en: {os.path.join(args.out, 'comparacion_planificador.csv')}")

    # Figura: distribucion de duracion de mision por planificador
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.5))
    axes[0].boxplot([base_exec["Mission_s"], oct_exec["Mission_s"]], tick_labels=["Manhattan", "Octile"])
    axes[0].set_ylabel("Duración de misión (s)")
    axes[0].set_title("Duración total por ejecución")

    axes[1].boxplot([base_exec["Planned_m"] / base_straight, oct_exec["Planned_m"] / oct_straight],
                     tick_labels=["Manhattan", "Octile"])
    axes[1].axhline(1.0, color="k", linestyle="--", linewidth=1, label="línea recta")
    axes[1].set_ylabel("Longitud planificada / línea recta")
    axes[1].set_title("Inflación de ruta planificada")
    axes[1].legend(fontsize=8)
    fig.suptitle("Manhattan (no admisible) vs Octile (admisible) — mismo controlador")
    fig.tight_layout()
    fig.savefig(os.path.join(args.out, "fig_comparacion_planificador.png"), dpi=150)
    print(f"Figura guardada en: {os.path.join(args.out, 'fig_comparacion_planificador.png')}")


if __name__ == "__main__":
    main()
