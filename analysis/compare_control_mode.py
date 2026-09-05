"""
Compara el modo de control holonomico contra el diferencial-emulado (linea
base), mismo circuito/planificador/tolerancia, para el punto #5 de los
revisores: cuantificar el tiempo recuperable al habilitar movimiento lateral.

Uso:
    python compare_control_mode.py <baseline_csv> <holonomic_csv> --out <carpeta>
"""
import argparse
import os

import pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from scipy import stats
import numpy as np


def load(path, label):
    df = pd.read_csv(path)
    df = df.rename(columns={
        "StraightLineDistance_m": "StraightLineDistance",
        "ExecutedPathLength_m": "ExecutedPathLength",
        "TimeRotating_s": "TimeRotating",
        "TimeTranslating_s": "TimeTranslating",
        "TimeConverging_s": "TimeConverging",
        "TimeReplanning_s": "TimeReplanning",
        "TotalSegmentTime_s": "TotalSegmentTime",
    })
    df["Label"] = label
    return df


def per_execution(df):
    return df.groupby("Execution").agg(
        Mission_s=("TotalSegmentTime", "sum"),
        Rotating_s=("TimeRotating", "sum"),
        Translating_s=("TimeTranslating", "sum"),
        Converging_s=("TimeConverging", "sum"),
        Replanning_s=("TimeReplanning", "sum"),
        Executed_m=("ExecutedPathLength", "sum"),
    )


def main():
    p = argparse.ArgumentParser()
    p.add_argument("baseline_csv")
    p.add_argument("holonomic_csv")
    p.add_argument("--out", required=True)
    args = p.parse_args()
    os.makedirs(args.out, exist_ok=True)

    base = load(args.baseline_csv, "DifferentialEmulated")
    holo = load(args.holonomic_csv, "Holonomic")

    base_exec = per_execution(base)
    holo_exec = per_execution(holo)

    t_mission = stats.ttest_ind(base_exec["Mission_s"], holo_exec["Mission_s"], equal_var=False)
    t_rotating = stats.ttest_ind(base_exec["Rotating_s"], holo_exec["Rotating_s"], equal_var=False)
    t_executed = stats.ttest_ind(base_exec["Executed_m"], holo_exec["Executed_m"], equal_var=False)

    recoverable_mean = base_exec["Mission_s"].mean() - holo_exec["Mission_s"].mean()
    recoverable_pct = 100 * recoverable_mean / base_exec["Mission_s"].mean()
    rotating_eliminated = base_exec["Rotating_s"].mean() - holo_exec["Rotating_s"].mean()

    print("=== COMPARACION DE CONTROL (punto #5 revisores) ===")
    print(f"N ejecuciones: Diferencial={base_exec.shape[0]}, Holonomico={holo_exec.shape[0]}")
    print(f"\nDuracion de mision: Diferencial {base_exec['Mission_s'].mean():.1f}+-{base_exec['Mission_s'].std():.1f} s "
          f"vs Holonomico {holo_exec['Mission_s'].mean():.1f}+-{holo_exec['Mission_s'].std():.1f} s")
    print(f"  Tiempo recuperable: {recoverable_mean:.1f} s ({recoverable_pct:.1f}% de la mision) "
          f"(t={t_mission.statistic:.2f}, p={t_mission.pvalue:.2e})")
    print(f"\nTiempo de reorientacion (TimeRotating): Diferencial {base_exec['Rotating_s'].mean():.1f} s "
          f"vs Holonomico {holo_exec['Rotating_s'].mean():.2f} s (eliminado: {rotating_eliminated:.1f} s, "
          f"t={t_rotating.statistic:.2f}, p={t_rotating.pvalue:.2e})")
    print(f"\nDistancia ejecutada total: Diferencial {base_exec['Executed_m'].mean():.2f} m "
          f"vs Holonomico {holo_exec['Executed_m'].mean():.2f} m (t={t_executed.statistic:.2f}, p={t_executed.pvalue:.3f})")

    summary = pd.DataFrame({
        "Metric": ["N_executions", "Mission_s_mean", "Mission_s_sd", "Rotating_s_mean", "Rotating_s_sd",
                   "Translating_s_mean", "Converging_s_mean", "Replanning_s_mean", "Executed_m_mean"],
        "DifferentialEmulated": [base_exec.shape[0], base_exec["Mission_s"].mean(), base_exec["Mission_s"].std(),
                                  base_exec["Rotating_s"].mean(), base_exec["Rotating_s"].std(),
                                  base_exec["Translating_s"].mean(), base_exec["Converging_s"].mean(),
                                  base_exec["Replanning_s"].mean(), base_exec["Executed_m"].mean()],
        "Holonomic": [holo_exec.shape[0], holo_exec["Mission_s"].mean(), holo_exec["Mission_s"].std(),
                      holo_exec["Rotating_s"].mean(), holo_exec["Rotating_s"].std(),
                      holo_exec["Translating_s"].mean(), holo_exec["Converging_s"].mean(),
                      holo_exec["Replanning_s"].mean(), holo_exec["Executed_m"].mean()],
    })
    summary.to_csv(os.path.join(args.out, "comparacion_control.csv"), index=False)
    print(f"\nTabla guardada en: {os.path.join(args.out, 'comparacion_control.csv')}")

    # Figuras
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.5))
    axes[0].boxplot([base_exec["Mission_s"], holo_exec["Mission_s"]], tick_labels=["Diferencial", "Holonómico"])
    axes[0].set_ylabel("Duración de misión (s)")
    axes[0].set_title("Duración total por ejecución")

    # Descomposicion apilada promedio
    labels = ["Diferencial", "Holonómico"]
    rotating = [base_exec["Rotating_s"].mean(), holo_exec["Rotating_s"].mean()]
    translating = [base_exec["Translating_s"].mean(), holo_exec["Translating_s"].mean()]
    converging = [base_exec["Converging_s"].mean(), holo_exec["Converging_s"].mean()]
    replanning = [base_exec["Replanning_s"].mean(), holo_exec["Replanning_s"].mean()]
    x = np.arange(2)
    axes[1].bar(x, translating, label="Traslación")
    axes[1].bar(x, rotating, bottom=translating, label="Reorientación")
    bottom2 = [translating[i] + rotating[i] for i in range(2)]
    axes[1].bar(x, converging, bottom=bottom2, label="Convergencia")
    bottom3 = [bottom2[i] + converging[i] for i in range(2)]
    axes[1].bar(x, replanning, bottom=bottom3, label="Replanificación")
    axes[1].set_xticks(x)
    axes[1].set_xticklabels(labels)
    axes[1].set_ylabel("Tiempo medio por misión (s)")
    axes[1].set_title("Descomposición: diferencial vs holonómico")
    axes[1].legend(fontsize=8)
    fig.suptitle("Diferencial-emulado vs Holonómico — mismo circuito/planificador/tolerancia")
    fig.tight_layout()
    fig.savefig(os.path.join(args.out, "fig_comparacion_control.png"), dpi=150)
    print(f"Figura guardada en: {os.path.join(args.out, 'fig_comparacion_control.png')}")


if __name__ == "__main__":
    main()
