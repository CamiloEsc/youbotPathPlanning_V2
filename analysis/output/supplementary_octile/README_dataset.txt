dataset_segmentos_circuito_base.csv
Material suplementario — decomposicion de costo por segmento
KUKA youBot, circuito base (config: circuito=CurrentCode, modo de control=
DifferentialEmulated, heuristica del planificador=Octile, tolerancia
de llegada=0.03 m).

90 filas = 9 ejecuciones completas x 10 segmentos, sin
colisiones ni fallas de planificacion. Ejecuciones incluidas (orden
cronologico, identificador interno sesion_corrida): 20260904_144847_1, 20260904_144847_2, 20260904_144847_3, 20260904_144847_4, 20260904_144847_5, 20260904_144847_6, 20260904_144847_7, 20260904_144847_9, 20260904_144847_10.

Cada fila es un segmento de un ciclo de 10 waypoints. "Execution" identifica
la repeticion (1-9); "Segment" identifica el segmento
(S1-S10) dentro de esa repeticion.

Columnas:
  Execution                 Numero de repeticion (1-9).
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
    costo axial 1.0 / diagonal 1.4; heuristica Octile.
  - Umbral de giro en el sitio (modo DifferentialEmulated): 0.4 rad.
  - Tolerancia de llegada: 0.03 m.
  - Codigo: VRepClient (version registrada en run_metadata.txt de cada
    ejecucion; ver repositorio del proyecto para el codigo exacto usado).
  - Filas descartadas antes de construir este archivo: ejecuciones que no
    completaron los 10 segmentos (colision o fallo de planificacion) y una
    fila de registro espuria ("S0") producida por un bug ya corregido en el
    software de control (no afecta a las ejecuciones incluidas aqui).
