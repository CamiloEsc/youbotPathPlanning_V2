"""
Genera el dataset final (material suplementario) del circuito base:
10 ejecuciones completas x 10 segmentos = 100 observaciones, depurado y
listo para reproducir las figuras/tablas del paper (y las nuevas que
responden a los revisores).

Toma las carpetas ExperimentData/<timestamp>/segment_summary.csv, filtra a
la configuracion base (circuito CurrentCode, control DifferentialEmulated,
heuristica Manhattan, tolerancia 0.03), descarta corridas incompletas
(colision/falla) y filas S0 espurias (bug de logging ya corregido), toma
las primeras 10 ejecuciones completas en orden cronologico, y escribe:

  - dataset_segmentos_circuito_base.csv  (100 filas, esquema final)
  - README_dataset.txt                   (diccionario de datos)

Uso:
    python build_supplementary_dataset.py <ExperimentData_root> --out <carpeta_salida>
        [--circuit NOMBRE] [--control-mode NOMBRE] [--planner NOMBRE]
        [--tolerance VALOR] [--n-executions N]
"""

import argparse
import glob
import os
import sys

import numpy as np
import pandas as pd


def load_segment_summaries(root):
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
            # solo encabezado (0 filas); sus columnas numericas quedan dtype
            # "object" y degradan el concat entero a texto si se incluye.
            continue
        df["Session"] = session
        df["GlobalRunID"] = session + "_" + df["RunID"].astype(str)
        frames.append(df)
    return pd.concat(frames, ignore_index=True)


def segment_order_key(seg):
    return int(seg[1:]) if seg.startswith("S") and seg[1:].isdigit() else 999


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("root")
    p.add_argument("--out", required=True)
    p.add_argument("--circuit", default="CurrentCode")
    p.add_argument("--control-mode", default="DifferentialEmulated")
    p.add_argument("--planner", default="Manhattan")
    p.add_argument("--tolerance", type=float, default=0.03)
    p.add_argument("--n-executions", type=int, default=10)
    args = p.parse_args()

    os.makedirs(args.out, exist_ok=True)

    df = load_segment_summaries(args.root)
    total_raw = len(df)

    df = df[df["Outcome"] == "Completed"]
    df = df[df["SegmentIndex"] != "S0"]
    df = df[df["CircuitName"] == args.circuit]
    df = df[df["ControlMode"] == args.control_mode]
    df = df[df["PlannerHeuristic"] == args.planner]
    df = df[np.isclose(df["ArrivalTolerance"], args.tolerance)]

    n_segments_expected = df["SegmentIndex"].nunique()
    seg_counts = df.groupby("GlobalRunID")["SegmentIndex"].nunique()
    complete_runs = seg_counts[seg_counts == n_segments_expected].index.tolist()

    # Filtro de calidad: una corrida capturada ANTES de corregir el bug de
    # PlannedPathLengthM (guardaba solo el ultimo replanteo cerca de la meta,
    # no el camino completo planificado al inicio del segmento) tiene ese
    # valor muy por debajo de la distancia en linea recta en varios segmentos
    # -- un camino real nunca deberia ser mas corto que la linea recta. Se
    # detecta y excluye automaticamente en vez de confiar en cual sesion es.
    valid_runs = []
    for run_id, g in df[df["GlobalRunID"].isin(complete_runs)].groupby("GlobalRunID"):
        bad = (g["PlannedPathLengthM"] < 0.5 * g["StraightLineDistance"]).sum()
        if bad > 0:
            print(f"Excluida {run_id}: PlannedPathLengthM inconsistente en {bad} segmento(s) "
                  f"(corrida previa al fix de ese bug).", file=sys.stderr)
            continue
        # En modo Holonomic, TimeRotating deberia ser siempre 0 (el robot nunca
        # gira en el sitio para reorientarse). Un valor >0 indica una corrida
        # capturada ANTES del fix de clasificacion de fase que confundia
        # "heading error grande mientras se traslada" con "girando en el sitio".
        if "Holonomic" in g["ControlMode"].unique() and (g["TimeRotating"] > 0.001).any():
            print(f"Excluida {run_id}: TimeRotating>0 en modo Holonomic "
                  f"(corrida previa al fix de clasificacion de fase).", file=sys.stderr)
            continue
        valid_runs.append(run_id)
    complete_runs = valid_runs

    # Orden cronologico: el nombre de sesion es un timestamp yyyyMMdd_HHmmss,
    # y RunID ya es secuencial dentro de cada sesion.
    def run_sort_key(run_id):
        session, rid = run_id.rsplit("_", 1)
        return (session, int(rid))

    complete_runs_sorted = sorted(complete_runs, key=run_sort_key)

    if len(complete_runs_sorted) < args.n_executions:
        print(f"ADVERTENCIA: solo hay {len(complete_runs_sorted)} ejecuciones completas "
              f"disponibles, se pidieron {args.n_executions}. Se usan todas las disponibles.",
              file=sys.stderr)
        chosen_runs = complete_runs_sorted
    else:
        chosen_runs = complete_runs_sorted[: args.n_executions]

    print(f"Ejecuciones completas disponibles: {len(complete_runs_sorted)}")
    print(f"Ejecuciones elegidas (orden cronologico, primeras {len(chosen_runs)}): {chosen_runs}")

    subset = df[df["GlobalRunID"].isin(chosen_runs)].copy()

    # Renumerar Execution 1..N en el orden cronologico elegido (no el RunID
    # crudo de cada sesion, que se reinicia en 1 cada vez que se abre la app).
    run_to_exec = {run: i + 1 for i, run in enumerate(chosen_runs)}
    subset["Execution"] = subset["GlobalRunID"].map(run_to_exec)

    subset = subset.sort_values(
        by=["Execution", "SegmentIndex"],
        key=lambda col: col.map(segment_order_key) if col.name == "SegmentIndex" else col
    )

    final = pd.DataFrame({
        "Execution": subset["Execution"],
        "Segment": subset["SegmentIndex"],
        "StraightLineDistance_m": subset["StraightLineDistance"].round(3),
        "HeadingProxy_deg": subset["HeadingProxyDeg"].round(1),
        "ExecutedPathLength_m": subset["ExecutedPathLength"].round(3),
        "IntegratedAbsYaw_deg": subset["IntegratedAbsYawDeg"].round(1),
        "PlannedPathLength_m": subset["PlannedPathLengthM"].round(3),
        "MinClearanceRadius_cells": subset["MinClearanceRadius"].round(0).astype(int),
        "TimeRotating_s": subset["TimeRotating"].round(3),
        "TimeTranslating_s": subset["TimeTranslating"].round(3),
        "TimeConverging_s": subset["TimeConverging"].round(3),
        "TimeReplanning_s": subset["TimeReplanning"].round(3),
        "TotalSegmentTime_s": subset["TotalSegmentTime"].round(3),
    })

    expected_rows = len(chosen_runs) * n_segments_expected
    assert len(final) == expected_rows, f"Se esperaban {expected_rows} filas, hay {len(final)}"
    assert final.isna().sum().sum() == 0, "El dataset final tiene valores faltantes"

    out_csv = os.path.join(args.out, "dataset_segmentos_circuito_base.csv")
    final.to_csv(out_csv, index=False, lineterminator="\n")
    print(f"\nDataset final: {len(final)} filas ({len(chosen_runs)} ejecuciones x {n_segments_expected} segmentos)")
    print(f"Guardado en: {out_csv}")

    readme_path = os.path.join(args.out, "README_dataset.txt")
    with open(readme_path, "w", encoding="utf-8") as f:
        f.write(build_readme(args, chosen_runs, len(final)))
    print(f"Diccionario de datos guardado en: {readme_path}")

    print(f"\n(De referencia: {total_raw} filas crudas totales en las sesiones bajo {args.root}, "
          f"antes de filtrar por Outcome/circuito/config/S0.)")


def build_readme(args, chosen_runs, n_rows):
    return f"""dataset_segmentos_circuito_base.csv
Material suplementario — decomposicion de costo por segmento
KUKA youBot, circuito base (config: circuito={args.circuit}, modo de control=
{args.control_mode}, heuristica del planificador={args.planner}, tolerancia
de llegada={args.tolerance} m).

{n_rows} filas = {len(chosen_runs)} ejecuciones completas x 10 segmentos, sin
colisiones ni fallas de planificacion. Ejecuciones incluidas (orden
cronologico, identificador interno sesion_corrida): {", ".join(chosen_runs)}.

Cada fila es un segmento de un ciclo de 10 waypoints. "Execution" identifica
la repeticion (1-{len(chosen_runs)}); "Segment" identifica el segmento
(S1-S10) dentro de esa repeticion.

Columnas:
  Execution                 Numero de repeticion (1-{len(chosen_runs)}).
  Segment                   Identificador de segmento (S1-S10).
  StraightLineDistance_m    Distancia euclidiana en linea recta entre el
                             waypoint anterior y el objetivo de este segmento (m).
  HeadingProxy_deg           |Δψ|: diferencia angular absoluta entre el rumbo
                             de este segmento y el del segmento anterior,
                             calculada solo a partir de las coordenadas de los
                             waypoints (no de la trayectoria ejecutada). 0 en
                             el primer segmento de cada ejecucion.
  ExecutedPathLength_m       Distancia realmente recorrida por el robot durante
                             el segmento (integral de |Δposicion| a 10 Hz).
                             Incluye cualquier desvio respecto a la linea recta.
  IntegratedAbsYaw_deg       Cambio de rumbo realmente acumulado durante la
                             ejecucion del segmento (integral de |Δrumbo| a
                             10 Hz), incluyendo micro-correcciones continuas
                             del controlador, no solo el cambio de rumbo neto
                             entre waypoints.
  PlannedPathLength_m        Longitud del primer camino que el planificador
                             A* encontro para este segmento (costo del camino
                             x 0.10 m/celda), antes de que el robot empezara
                             a moverse.
  MinClearanceRadius_cells   Holgura minima a lo largo del camino ejecutado:
                             el mayor R (en celdas de 0.10 m) tal que una
                             ventana (2R+1)x(2R+1) centrada en cada celda del
                             camino esta completamente libre de obstaculos.
                             El planificador exige R>=3 para admitir cualquier
                             nodo; valores mayores indican mas margen real.
  TimeRotating_s              Tiempo con error de rumbo |φ| por encima del
                             umbral de giro en el sitio (0.4 rad).
  TimeTranslating_s           Tiempo avanzando hacia el objetivo (fuera de la
                             banda de convergencia final, ver ConvergenceBandRadius
                             en experiment.cfg = 1.0 m).
  TimeConverging_s            Tiempo dentro de la banda de convergencia final
                             (< 1.0 m del objetivo), donde el controlador
                             amortigua la velocidad para la aproximacion final.
  TimeReplanning_s            Tiempo en el que no habia un camino valido
                             disponible (buscando ruta / girando en el sitio
                             mientras se recalcula).
  TotalSegmentTime_s          Duracion total del segmento, desde que se activa
                             como objetivo hasta que se cumple el criterio de
                             llegada (= suma de las 4 columnas de tiempo
                             anteriores).

Notas metodologicas:
  - Tiempo: wall-clock de System.Diagnostics.Stopwatch (lado cliente C#),
    muestreado en cada tick de un temporizador de ~10 Hz (100 ms). No es
    tiempo interno de simulacion de CoppeliaSim.
  - Simulador: CoppeliaSim EDU 4.5, motor de fisica Bullet 2.78, paso de
    50 ms.
  - Resolucion de grilla de ocupacion: 0.10 m/celda; A* de 8 conexiones,
    costo axial 1.0 / diagonal 1.4; heuristica {args.planner}.
  - Umbral de giro en el sitio (modo {args.control_mode}): 0.4 rad.
  - Tolerancia de llegada: {args.tolerance} m.
  - Codigo: VRepClient (version registrada en run_metadata.txt de cada
    ejecucion; ver repositorio del proyecto para el codigo exacto usado).
  - Filas descartadas antes de construir este archivo: ejecuciones que no
    completaron los 10 segmentos (colision o fallo de planificacion) y una
    fila de registro espuria ("S0") producida por un bug ya corregido en el
    software de control (no afecta a las ejecuciones incluidas aqui).
"""


if __name__ == "__main__":
    main()
