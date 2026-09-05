"""Calcula las estadisticas finales EXACTAS sobre los datasets de material
suplementario (los mismos que se comparten), para citar en el paper con
numeros que coincidan exactamente con los datos publicados."""
import sys
import numpy as np
import pandas as pd
from scipy import stats
import statsmodels.formula.api as smf


def analyze(path, label):
    df = pd.read_csv(path)
    df = df.rename(columns={
        "StraightLineDistance_m": "StraightLineDistance",
        "HeadingProxy_deg": "HeadingProxyDeg",
        "ExecutedPathLength_m": "ExecutedPathLength",
        "IntegratedAbsYaw_deg": "IntegratedAbsYawDeg",
        "PlannedPathLength_m": "PlannedPathLengthM",
        "MinClearanceRadius_cells": "MinClearanceRadius",
        "TimeRotating_s": "TimeRotating",
        "TimeTranslating_s": "TimeTranslating",
        "TimeConverging_s": "TimeConverging",
        "TimeReplanning_s": "TimeReplanning",
        "TotalSegmentTime_s": "TotalSegmentTime",
    })
    n_exec = df["Execution"].nunique()
    n_seg = df["Segment"].nunique()

    raw = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=df).fit()
    means = df.groupby("Segment").agg(
        TotalSegmentTime=("TotalSegmentTime", "mean"),
        StraightLineDistance=("StraightLineDistance", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        MinClearanceRadius=("MinClearanceRadius", "mean"),
    ).reset_index()
    means_fit = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg", data=means).fit()

    # Lack of fit
    pure_error_ss = 0.0
    for seg, g in df.groupby("Segment"):
        pure_error_ss += ((g["TotalSegmentTime"] - g["TotalSegmentTime"].mean()) ** 2).sum()
    df_pure_error = len(df) - n_seg
    grand_ss = raw.ssr
    lof_ss = grand_ss - pure_error_ss
    df_lof = n_seg - 3
    ms_lof = lof_ss / df_lof
    ms_pe = pure_error_ss / df_pure_error
    f_lof = ms_lof / ms_pe
    p_lof = 1 - stats.f.cdf(f_lof, df_lof, df_pure_error)
    pct_systematic = 100 * lof_ss / grand_ss

    # Clustering by execution
    df2 = df.copy()
    df2["resid"] = raw.resid
    groups = [g["resid"].to_numpy(dtype=float) for _, g in df2.groupby("Execution")]
    f_ex, p_ex = stats.f_oneway(*groups)
    mission = df.groupby("Execution")["TotalSegmentTime"].sum()

    # Attribution
    alpha = raw.params["Intercept"]
    beta = raw.params["StraightLineDistance"]
    gamma = raw.params["HeadingProxyDeg"]
    total_d = df.groupby("Segment")["StraightLineDistance"].mean().sum()
    total_dpsi = df.groupby("Segment")["HeadingProxyDeg"].mean().sum()
    fixed_total = alpha * n_seg
    trans_total = beta * total_d
    rot_total = gamma * total_dpsi
    predicted = fixed_total + trans_total + rot_total

    # Instrumented validation
    seg_stats = df.groupby("Segment").agg(
        StraightLineDistance=("StraightLineDistance", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        TimeTranslating=("TimeTranslating", "mean"),
        TimeRotating=("TimeRotating", "mean"),
    ).reset_index()
    seg_stats["ModelTranslation"] = beta * seg_stats["StraightLineDistance"]
    seg_stats["ModelRotation"] = gamma * seg_stats["HeadingProxyDeg"]
    r_trans = np.corrcoef(seg_stats["ModelTranslation"], seg_stats["TimeTranslating"])[0, 1]
    r_rot = np.corrcoef(seg_stats["ModelRotation"], seg_stats["TimeRotating"])[0, 1]

    # Proxy error
    proxy = df.groupby("Segment").agg(
        StraightLineDistance=("StraightLineDistance", "mean"),
        ExecutedPathLength=("ExecutedPathLength", "mean"),
        HeadingProxyDeg=("HeadingProxyDeg", "mean"),
        IntegratedAbsYawDeg=("IntegratedAbsYawDeg", "mean"),
    ).reset_index()
    proxy["DistRatio"] = proxy["ExecutedPathLength"] / proxy["StraightLineDistance"]
    proxy["YawRatio"] = proxy["IntegratedAbsYawDeg"] / proxy["HeadingProxyDeg"].replace(0, np.nan)

    # Clearance covariate
    ext = smf.ols("TotalSegmentTime ~ StraightLineDistance + HeadingProxyDeg + MinClearanceRadius", data=means).fit()

    print(f"\n========== {label} (N={n_exec} ejecuciones x {n_seg} segmentos = {len(df)} obs) ==========")
    print(f"alpha={alpha:.3f} s (raw p={raw.pvalues['Intercept']:.4f}; means p={means_fit.pvalues['Intercept']:.4f})")
    print(f"beta={beta:.4f} s/m (raw p={raw.pvalues['StraightLineDistance']:.2e}; means p={means_fit.pvalues['StraightLineDistance']:.4f})")
    print(f"gamma={gamma:.5f} s/deg (raw p={raw.pvalues['HeadingProxyDeg']:.4f}; means p={means_fit.pvalues['HeadingProxyDeg']:.4f})")
    print(f"R2 raw={raw.rsquared:.4f} (RMSE={np.sqrt(raw.mse_resid):.3f}, F({raw.df_model:.0f},{raw.df_resid:.0f})={raw.fvalue:.1f})")
    print(f"R2 means={means_fit.rsquared:.4f} (RMSE={np.sqrt(means_fit.mse_resid):.3f})")
    ci_a = raw.conf_int().loc["Intercept"]
    ci_b = means_fit.conf_int().loc["StraightLineDistance"]
    ci_g = means_fit.conf_int().loc["HeadingProxyDeg"]
    ci_a_m = means_fit.conf_int().loc["Intercept"]
    print(f"95% CI alpha(means)=[{ci_a_m[0]:.2f},{ci_a_m[1]:.2f}], beta(means)=[{ci_b[0]:.3f},{ci_b[1]:.3f}], gamma(means)=[{ci_g[0]:.4f},{ci_g[1]:.4f}]")
    print(f"Pure error SD={np.sqrt(ms_pe):.3f} s (df={df_pure_error}); Lack-of-fit F({df_lof},{df_pure_error})={f_lof:.2f}, p={p_lof:.2e}; {pct_systematic:.1f}% sistematico")
    print(f"Clustering entre ejecuciones: F({n_exec-1},{len(df)-n_exec})={f_ex:.3f}, p={p_ex:.4f}")
    print(f"Mision: media={mission.mean():.1f} s, DE={mission.std():.2f} s, CV={100*mission.std()/mission.mean():.2f}%")
    print(f"Atribucion: traslacion={trans_total:.1f} s ({100*trans_total/predicted:.1f}%), "
          f"overhead fijo={fixed_total:.1f} s ({100*fixed_total/predicted:.1f}%), "
          f"reorientacion={rot_total:.1f} s ({100*rot_total/predicted:.1f}%); prediccion total={predicted:.1f} s")
    print(f"Validacion instrumentada: r(traslacion)={r_trans:.3f}, r(rotacion)={r_rot:.3f}")
    print(f"Proxies: ratio distancia media={proxy['DistRatio'].mean():.3f} max={proxy['DistRatio'].max():.3f}; "
          f"ratio yaw media={proxy['YawRatio'].mean():.3f} max={proxy['YawRatio'].max():.3f}")
    print(f"Holgura: R2 base={means_fit.rsquared:.4f} -> R2 extendido={ext.rsquared:.4f}, "
          f"coef={ext.params.get('MinClearanceRadius', float('nan')):.4f}, p={ext.pvalues.get('MinClearanceRadius', float('nan')):.4f}")
    return dict(alpha=alpha, beta=beta, gamma=gamma, r2_raw=raw.rsquared, r2_means=means_fit.rsquared)


if __name__ == "__main__":
    for path, label in [
        ("./output/supplementary/dataset_segmentos_circuito_base.csv", "BASELINE Manhattan/Differential"),
        ("./output/supplementary_holonomic/dataset_segmentos_circuito_base.csv", "HOLONOMIC"),
        ("./output/supplementary_circuit2/dataset_segmentos_circuito_base.csv", "CIRCUIT2"),
    ]:
        analyze(path, label)
