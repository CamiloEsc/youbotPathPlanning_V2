"""
Analiza el barrido de tolerancia de llegada (punto #4 de los revisores):
compara el tiempo de convergencia final (TimeConverging) y la duracion de
mision entre distintos valores de ArrivalTolerance, con todo lo demas fijo
(circuito CurrentCode, control DifferentialEmulated, planificador Manhattan).

Uso:
    python analyze_tolerance_sweep.py <ExperimentData_root> --out <carpeta>
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


def load_all(root):
    paths = sorted(glob.glob(os.path.join(root, "**", "segment_summary.csv"), recursive=True))
    frames = []
    for p in paths:
        session = os.path.basename(os.path.dirname(p))
        d = pd.read_csv(p)
        if d.empty:
            continue
        d["Session"] = session
        d["GlobalRunID"] = session + "_" + d["RunID"].astype(str)
        frames.append(d)
    return pd.concat(frames, ignore_index=True)


def clean_complete_runs(df):
    df = df[(df["Outcome"] == "Completed") & (df["SegmentIndex"] != "S0")].copy()
    n_segments_expected = df["SegmentIndex"].nunique()
    seg_counts = df.groupby("GlobalRunID")["SegmentIndex"].nunique()
    complete = seg_counts[seg_counts == n_segments_expected].index
    return df[df["GlobalRunID"].isin(complete)]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("root")
    p.add_argument("--out", required=True)
    p.add_argument("--circuit", default="CurrentCode")
    p.add_argument("--control-mode", default="DifferentialEmulated")
    p.add_argument("--planner", default="Manhattan")
    args = p.parse_args()
    os.makedirs(args.out, exist_ok=True)

    all_df = load_all(args.root)
    df = all_df[(all_df["CircuitName"] == args.circuit) &
                (all_df["ControlMode"] == args.control_mode) &
                (all_df["PlannerHeuristic"] == args.planner)]
    df = clean_complete_runs(df)

    tolerances = sorted(df["ArrivalTolerance"].unique())
    print(f"Valores de tolerancia encontrados: {tolerances}")

    rows = []
    per_exec_records = []
    for tol in tolerances:
        sub = df[np.isclose(df["ArrivalTolerance"], tol)]
        per_exec = sub.groupby("GlobalRunID").agg(
            Mission_s=("TotalSegmentTime", "sum"),
            Converging_s=("TimeConverging", "sum"),
            Rotating_s=("TimeRotating", "sum"),
            Translating_s=("TimeTranslating", "sum"),
            Replanning_s=("TimeReplanning", "sum"),
        )
        per_exec["Tolerance"] = tol
        per_exec_records.append(per_exec)
        rows.append({
            "ArrivalTolerance_m": tol,
            "N_executions": per_exec.shape[0],
            "Mission_s_mean": per_exec["Mission_s"].mean(),
            "Mission_s_sd": per_exec["Mission_s"].std(),
            "Converging_s_mean": per_exec["Converging_s"].mean(),
            "Converging_s_sd": per_exec["Converging_s"].std(),
            "Rotating_s_mean": per_exec["Rotating_s"].mean(),
            "Translating_s_mean": per_exec["Translating_s"].mean(),
            "Replanning_s_mean": per_exec["Replanning_s"].mean(),
        })

    table = pd.DataFrame(rows)
    table.to_csv(os.path.join(args.out, "barrido_tolerancia.csv"), index=False)
    print("\n=== BARRIDO DE TOLERANCIA (punto #4 revisores) ===")
    print(table.to_string(index=False))

    all_exec = pd.concat(per_exec_records)
    tol_arr = np.asarray(all_exec["Tolerance"].to_numpy(), dtype=np.float64)
    conv_arr = np.asarray(all_exec["Converging_s"].to_numpy(), dtype=np.float64)
    mission_arr = np.asarray(all_exec["Mission_s"].to_numpy(), dtype=np.float64)

    # Correlacion / regresion simple: Converging_s vs tolerancia
    slope, intercept, r, pval, se = stats.linregress(tol_arr, conv_arr)
    print(f"\nRegresion TimeConverging ~ ArrivalTolerance: pendiente={slope:.2f} s/m, "
          f"r={r:.3f}, p={pval:.4f}")
    print("(pendiente negativa y significativa confirmaria la hipotesis del paper: "
          "tolerancias mas flexibles reducen el overhead de convergencia)")

    slope_m, intercept_m, r_m, pval_m, se_m = stats.linregress(tol_arr, mission_arr)
    print(f"Regresion Mission_s ~ ArrivalTolerance: pendiente={slope_m:.2f} s/m, "
          f"r={r_m:.3f}, p={pval_m:.4f}")

    # Figuras
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.5))
    axes[0].errorbar(table["ArrivalTolerance_m"], table["Converging_s_mean"],
                      yerr=table["Converging_s_sd"], marker="o", capsize=4)
    axes[0].set_xlabel("Tolerancia de llegada (m)")
    axes[0].set_ylabel("Tiempo de convergencia total por misión (s)")
    axes[0].set_title("Overhead de convergencia vs tolerancia")

    axes[1].errorbar(table["ArrivalTolerance_m"], table["Mission_s_mean"],
                      yerr=table["Mission_s_sd"], marker="o", capsize=4, color="tab:orange")
    axes[1].set_xlabel("Tolerancia de llegada (m)")
    axes[1].set_ylabel("Duración de misión (s)")
    axes[1].set_title("Duración total vs tolerancia")
    fig.suptitle("Barrido de tolerancia de llegada — circuito base, Manhattan")
    fig.tight_layout()
    fig.savefig(os.path.join(args.out, "fig_barrido_tolerancia.png"), dpi=150)
    print(f"\nFigura guardada en: {os.path.join(args.out, 'fig_barrido_tolerancia.png')}")
    print(f"Tabla guardada en: {os.path.join(args.out, 'barrido_tolerancia.csv')}")


if __name__ == "__main__":
    main()
