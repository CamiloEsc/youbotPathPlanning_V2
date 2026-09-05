using System;
using System.Collections.Generic;
using System.Diagnostics;   // Necesario para medir tiempo
using System.Drawing;
using System.Globalization; // Necesario para formateo numérico invariante en CSV
using System.IO;            // Necesario para guardar archivos
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VRepAdapter;

namespace VRepClient
{
    public partial class Form1 : Form
    {
        public class MissionPoint
        {
            public string Name { get; set; }
            public PointF Coords { get; set; }
            public string Status { get; set; }
        }

        public Form1()
        {
            InitializeComponent();
            f1 = this;

            this.WindowState = FormWindowState.Maximized;
            this.SetStyle(ControlStyles.DoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();

            // --- CORRECCIÓN VISUAL ---
            // Usamos ZOOM para que el mapa quepa siempre en la pantalla
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.BackColor = Color.White;

            CrearPanelInfo();
            panelInfo.Dock = DockStyle.Left;

            pnlMapContainer = new Panel();
            pnlMapContainer.Parent = this;
            pnlMapContainer.Dock = DockStyle.Fill;
            pnlMapContainer.BackColor = Color.Gray;
            pnlMapContainer.BorderStyle = BorderStyle.Fixed3D;
            pnlMapContainer.BringToFront();

            pictureBox1.Parent = pnlMapContainer;
            // Hacemos que el PictureBox ocupe todo el panel para aprovechar el modo Zoom
            pictureBox1.Dock = DockStyle.Fill;
        }

        // --- UI ---
        Panel panelInfo;
        Panel pnlMapContainer;
        DataGridView gridMisiones;
        Label lblPosX, lblPosY, lblStatus, lblDistancia, lblRunInfo;

        // --- VARIABLES CIENTÍFICAS ---
        const string ExperimentCodeVersion = "VRepClient-reviewer-response-v1";
        int currentRun = 1;
        bool isLogging = false;
        StreamWriter logger;
        StreamWriter segmentLogger;
        StreamWriter eventsLogger;
        string currentRunFolder;
        Stopwatch stopwatch;

        // --- CONFIGURACIÓN DE EXPERIMENTO (experiment.cfg) ---
        ExperimentConfig cfg;
        int circuitSeqIndex = 0;
        int repetitionInCircuit = 1;
        string currentCircuitName;
        int segmentIndexInCircuit = 0;
        float[] segStraightDist;
        float[] segHeadingProxyDeg;

        // --- ACUMULADORES POR SEGMENTO (instrumentación, puntos #1/#2/#3/#9 revisores) ---
        double timeRotating, timeTranslating, timeConverging, timeReplanning;
        float executedPathLength;
        float integratedAbsYawDeg;
        float segmentMinClearanceRadius;
        float lastPlannedPathCost;
        bool havePlannedPathForSegment;
        float segmentMinDistToGoal;
        PointF lastOdomPos;
        float lastHeadingForYaw;
        bool haveLastSample;
        long lastTickMs;

        // --- SISTEMA ---
        public RobotAdapter ra;
        public Drive RobDrive;
        public SequencePoints SQ;
        public Map map;
        public SearchInGraph SiG;

        public int Enviar = 0;
        public int Detener = 1;
        public List<Point> ListPoints = new List<Point>();
        public static Form1 f1;

        // --- GRÁFICOS ---
        Graphics g;
        Bitmap mapImage;
        Bitmap staticMapLayer;
        Graphics gStatic;

        // Variable dinámica
        int currentCellSize = 5;
        int lastDrawnIndex = 0;
        bool visualEnabled = true;

        // --- GESTIÓN DE MISIÓN ---
        Queue<MissionPoint> MissionQueue = new Queue<MissionPoint>();
        MissionPoint CurrentMission;
        bool HasGoal = false;

        bool isCalculating = false;
        DateTime lastPathCalc = DateTime.MinValue;
        DateTime lastMapUpdate = DateTime.MinValue;
        float lastRot = 0;

        DateTime stuckTimer = DateTime.MinValue;
        PointF lastPos = new PointF(0, 0);
        int pathFailCount = 0;

        private void CrearPanelInfo()
        {
            panelInfo = new Panel();
            panelInfo.Parent = this;
            panelInfo.Width = 320;
            panelInfo.BackColor = Color.FromArgb(240, 240, 240);
            panelInfo.BorderStyle = BorderStyle.FixedSingle;

            Label lblTitle = new Label { Parent = panelInfo, Text = "CONTROL EXPERIMENTAL", Font = new Font("Segoe UI", 14, FontStyle.Bold), Top = 15, Left = 10, AutoSize = true };

            lblRunInfo = new Label { Parent = panelInfo, Text = "RUN: 0 / 30", ForeColor = Color.DarkBlue, Font = new Font("Segoe UI", 11, FontStyle.Bold), Top = 50, Left = 10, AutoSize = true };

            gridMisiones = new DataGridView();
            gridMisiones.Parent = panelInfo;
            gridMisiones.Top = 80;
            gridMisiones.Left = 10;
            gridMisiones.Width = 295;
            gridMisiones.Height = 350;
            gridMisiones.ReadOnly = true;
            gridMisiones.RowHeadersVisible = false;
            gridMisiones.AllowUserToAddRows = false;
            gridMisiones.BackgroundColor = Color.White;
            gridMisiones.BorderStyle = BorderStyle.None;
            gridMisiones.ColumnCount = 3;
            gridMisiones.Columns[0].Name = "ID"; gridMisiones.Columns[0].Width = 40;
            gridMisiones.Columns[1].Name = "Pos"; gridMisiones.Columns[1].Width = 100;
            gridMisiones.Columns[2].Name = "Estado"; gridMisiones.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            Panel pnlCoords = new Panel { Parent = panelInfo, Top = 450, Left = 10, Width = 295, Height = 140, BackColor = Color.Black };
            Label lblLive = new Label { Parent = pnlCoords, Text = "DATOS EN VIVO", ForeColor = Color.Lime, Font = new Font("Consolas", 10), Top = 5, Left = 5, AutoSize = true };
            lblPosX = new Label { Parent = pnlCoords, Text = "X: 0.00", ForeColor = Color.Cyan, Font = new Font("Consolas", 14, FontStyle.Bold), Top = 30, Left = 10, AutoSize = true };
            lblPosY = new Label { Parent = pnlCoords, Text = "Y: 0.00", ForeColor = Color.Cyan, Font = new Font("Consolas", 14, FontStyle.Bold), Top = 60, Left = 10, AutoSize = true };
            lblDistancia = new Label { Parent = pnlCoords, Text = "Dist: 0.00 m", ForeColor = Color.Yellow, Font = new Font("Consolas", 14, FontStyle.Bold), Top = 90, Left = 10, AutoSize = true };

            lblStatus = new Label { Parent = panelInfo, Text = "ESPERANDO...", Font = new Font("Segoe UI", 11, FontStyle.Bold), Top = 600, Left = 10, AutoSize = true, ForeColor = Color.DimGray };
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (ra == null) ra = new VrepAdapter();
            ra.Init();
            lblStatus.Text = "SISTEMA CONECTADO";
            lblStatus.ForeColor = Color.DarkGreen;
        }

        private void VrepAdapter_Click(object sender, EventArgs e) { ra = new VrepAdapter(); }

        private void Drive_Click(object sender, EventArgs e)
        {
            // 1. LIMPIEZA TOTAL
            LimpiarMemoriaGrafica();
            visualEnabled = true;

            // 2. INICIALIZACIÓN
            SQ = new SequencePoints();
            RobDrive = new Drive();
            map = new Map();
            SiG = new SearchInGraph();

            cfg = ExperimentConfig.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "experiment.cfg"));
            ApplyConfigToComponents();

            // 3. CÁLCULO SEGURO DE DIMENSIONES (FIX DEFINITIVO)
            // Límite máximo de pixeles por lado para que quepa en RAM
            int MAX_PIXELS = 2500;

            // Obtenemos dimensiones lógicas del mapa
            int logicW = Math.Max(50, map.Ymax + 20);
            int logicH = Math.Max(50, map.Xmax + 20);

            // Calculamos el CellSize para que la imagen NUNCA supere MAX_PIXELS
            // Ejemplo: Si mapa es 500x500, CellSize será ~5. Si es 5000x5000, será 1.
            int posibleSizeW = MAX_PIXELS / logicW;
            int posibleSizeH = MAX_PIXELS / logicH;

            currentCellSize = Math.Min(posibleSizeW, posibleSizeH);

            // Límites lógicos del tamaño de celda
            if (currentCellSize < 1) currentCellSize = 1;
            if (currentCellSize > 10) currentCellSize = 10; // Tope máximo para que no se vea gigante

            int finalW = logicW * currentCellSize;
            int finalH = logicH * currentCellSize;

            // Recorte de seguridad final (por si acaso)
            if (finalW > MAX_PIXELS) finalW = MAX_PIXELS;
            if (finalH > MAX_PIXELS) finalH = MAX_PIXELS;

            try
            {
                mapImage = new Bitmap(finalW, finalH);
                // Asignamos la imagen al PictureBox
                pictureBox1.Image = mapImage;
                g = Graphics.FromImage(mapImage);

                staticMapLayer = new Bitmap(finalW, finalH);
                gStatic = Graphics.FromImage(staticMapLayer);
                gStatic.Clear(Color.White);
            }
            catch
            {
                visualEnabled = false;
                lblStatus.Text = "MODO SIN GRÁFICOS (RAM)";
                LimpiarMemoriaGrafica();
            }

            lastDrawnIndex = 0;
            RedrawStaticObstacles();
        }

        private void LimpiarMemoriaGrafica()
        {
            try
            {
                if (pictureBox1.Image != null) pictureBox1.Image = null;
                if (mapImage != null) { mapImage.Dispose(); mapImage = null; }
                if (staticMapLayer != null) { staticMapLayer.Dispose(); staticMapLayer = null; }
                if (g != null) { g.Dispose(); g = null; }
                if (gStatic != null) { gStatic.Dispose(); gStatic = null; }
                GC.Collect();
            }
            catch { }
        }

        // Aplica los parámetros de ExperimentConfig a los componentes ya construidos
        // (Drive, SequencePoints, SearchInGraph, RobotAdapter). Se llama al iniciar
        // (Drive_Click) y de nuevo aquí por si ra se conectó después.
        private void ApplyConfigToComponents()
        {
            if (cfg == null) return;
            if (RobDrive != null)
            {
                RobDrive.ArrivalTolerance = cfg.ArrivalTolerance;
                RobDrive.RotationThreshold = cfg.RotationThreshold;
                RobDrive.VxSign = cfg.HolonomicVxSign;
                RobDrive.VySign = cfg.HolonomicVySign;
                RobDrive.OmegaSign = cfg.HolonomicOmegaSign;
            }
            if (SQ != null)
            {
                SQ.LookaheadCells = cfg.LookaheadCells;
                SQ.LookaheadAdvance = cfg.LookaheadAdvance;
            }
            if (SiG != null)
            {
                SiG.Heuristic = cfg.PlannerHeuristic;
            }
            if (ra != null)
            {
                ra.WheelGain = cfg.WheelGain;
            }
        }

        // Crea una carpeta nueva por corrida, con marca de tiempo, para que ningún
        // experimento sobrescriba al anterior (antes "datos_experimento_n30.csv" se
        // truncaba en cada ENVIAR, perdiendo corridas previas). Todo lo de una corrida
        // (CSV por tick, resumen por segmento, eventos, metadatos) queda junto ahí.
        private string EnsureRunFolder()
        {
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExperimentData", ts);
            Directory.CreateDirectory(folder);
            return folder;
        }

        // Vuelca la configuración activa y metadatos de trazabilidad (versión de
        // código, intervalo real del timer, base de tiempo) a un archivo de texto,
        // uno por corrida — responde al punto #10 de los revisores (reproducibilidad).
        private void WriteRunMetadata()
        {
            try
            {
                string path = Path.Combine(currentRunFolder, "run_metadata.txt");
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("Metadatos de trazabilidad del experimento");
                    w.WriteLine("CodeVersion=" + ExperimentCodeVersion);
                    w.WriteLine("FechaHoraInicio=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                    w.WriteLine("TimerIntervalMs=" + timer1.Interval.ToString(CultureInfo.InvariantCulture));
                    w.WriteLine("NotaTiempo=Tiempo wall-clock de System.Diagnostics.Stopwatch (lado C#), muestreado en cada tick de timer1 (~10 Hz). No es tiempo interno de simulación de CoppeliaSim.");
                    w.WriteLine();
                    w.Write(cfg.ToMetadataString());
                }
            }
            catch { }
        }

        // Registra un evento puntual (inicio/fin de corrida, atasco detectado, sin
        // ruta encontrada, detención manual) con la posición y el objetivo del robot
        // en ese instante — para poder reconstruir después qué pasó exactamente en
        // una colisión o falla, sin depender de mirar la simulación en vivo.
        private void LogEvent(string eventType, string detail)
        {
            if (eventsLogger == null) return;
            try
            {
                double elapsed = (stopwatch != null) ? stopwatch.Elapsed.TotalSeconds : 0;
                float rx = 0, ry = 0, tx = 0, ty = 0;
                if (ra != null && ra.RobotOdomData != null) { rx = ra.RobotOdomData[0]; ry = ra.RobotOdomData[1]; }
                if (CurrentMission != null) { tx = CurrentMission.Coords.X; ty = CurrentMission.Coords.Y; }
                var fields = new string[]
                {
                    DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                    elapsed.ToString("F3", CultureInfo.InvariantCulture),
                    eventType,
                    currentRun.ToString(CultureInfo.InvariantCulture),
                    currentCircuitName ?? "",
                    segmentIndexInCircuit.ToString(CultureInfo.InvariantCulture),
                    rx.ToString("F3", CultureInfo.InvariantCulture),
                    ry.ToString("F3", CultureInfo.InvariantCulture),
                    tx.ToString("F3", CultureInfo.InvariantCulture),
                    ty.ToString("F3", CultureInfo.InvariantCulture),
                    detail ?? ""
                };
                eventsLogger.WriteLine(string.Join(",", fields));
                eventsLogger.Flush();
            }
            catch { }
        }

        // --- EXPERIMENTO ---
        private void button4_Click(object sender, EventArgs e)
        {
            Enviar = 1; Detener = 0;
            if (cfg == null) cfg = ExperimentConfig.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "experiment.cfg"));
            ApplyConfigToComponents();
            currentRun = 1;
            circuitSeqIndex = 0;
            repetitionInCircuit = 1;
            currentCircuitName = cfg.CircuitSequence.Count > 0 ? cfg.CircuitSequence[0] : "PaperTable1";
            try
            {
                currentRunFolder = EnsureRunFolder();
                logger = new StreamWriter(Path.Combine(currentRunFolder, "datos_experimento.csv"));
                logger.WriteLine("RunID,CircuitName,Tiempo(s),RobotX,RobotY,RobotHeadingRad,MetaX,MetaY,Error(m)");
                segmentLogger = new StreamWriter(Path.Combine(currentRunFolder, "segment_summary.csv"));
                segmentLogger.WriteLine("RunID,CircuitName,SegmentIndex,ControlMode,PlannerHeuristic,ArrivalTolerance,StraightLineDistance,PlannedPathLengthM,ExecutedPathLength,HeadingProxyDeg,IntegratedAbsYawDeg,MinClearanceRadius,TimeRotating,TimeTranslating,TimeConverging,TimeReplanning,TotalSegmentTime,Outcome");
                eventsLogger = new StreamWriter(Path.Combine(currentRunFolder, "events.csv"));
                eventsLogger.WriteLine("WallClockTime,ElapsedS,EventType,RunID,CircuitName,SegmentIndex,RobotX,RobotY,TargetX,TargetY,Detail");
                WriteRunMetadata();
                stopwatch = new Stopwatch();
                stopwatch.Start();
                isLogging = true;
                LogEvent("EXPERIMENT_START", "Carpeta: " + currentRunFolder);
                lblStatus.Text = "GUARDANDO EN: " + currentRunFolder;
            }
            catch (Exception ex) { MessageBox.Show("Error log: " + ex.Message); }
            CargarYArrancarMision();
        }

        private void CargarYArrancarMision()
        {
            // NOTA: se probó resetear el mapa al empezar cada repetición (para
            // evitar que el ruido de LIDAR/odometría se acumule en waypoints
            // revisitados como (0,4)). Revertido: en 2 sesiones distintas eso
            // hizo que la repetición 2 se trabara SIEMPRE en las mismas
            // coordenadas exactas, algo que nunca pasaba sin el reset (las
            // sesiones aguantaban 3-4 repeticiones limpias antes de fallar).
            // El mapa persistente durante toda la sesión (comportamiento
            // original) rinde mejor en la práctica pese al riesgo teórico de
            // acumulación — queda como limitación a documentar, no a "arreglar"
            // a ciegas otra vez.

            MissionQueue.Clear();
            gridMisiones.Rows.Clear();
            lblRunInfo.Text = $"RUN: {currentRun} | {currentCircuitName} ({repetitionInCircuit}/{cfg.RepetitionsPerCircuit})";
            lblStatus.Text = $"RUN {currentRun} INICIADO ({currentCircuitName})";
            lblStatus.ForeColor = Color.Blue;

            List<PointF> coords;
            if (cfg.Circuits == null || !cfg.Circuits.TryGetValue(currentCircuitName, out coords) || coords == null || coords.Count == 0)
                coords = ExperimentConfig.Default().Circuits["PaperTable1"];

            // El primer segmento se mide desde la posición REAL de arranque del robot,
            // no desde un supuesto de circuito cerrado (ese supuesto daba 4.00 m para
            // S1 cuando la Tabla 1 del paper reporta 5.00 m — el robot no arranca en
            // el último waypoint del circuito).
            PointF startPos = (ra != null && ra.RobotOdomData != null)
                ? new PointF(ra.RobotOdomData[0], ra.RobotOdomData[1])
                : coords[coords.Count - 1];
            ComputeSegmentGeometry(startPos, coords, out segStraightDist, out segHeadingProxyDeg);
            segmentIndexInCircuit = 0;
            // Sin esto, LoadNextWaypoint() ve el CurrentMission de la repetición
            // ANTERIOR todavía asignado, entra a la rama "hay misión previa que
            // cerrar" y escribe una fila S0 duplicada con los datos ya viejos del
            // último segmento de la corrida anterior (se veía en segment_summary.csv
            // como una fila extra "S0" idéntica al último S10).
            CurrentMission = null;

            for (int i = 0; i < coords.Count; i++)
            {
                var mp = new MissionPoint { Name = "P" + (i + 1), Coords = coords[i], Status = "Pendiente" };
                MissionQueue.Enqueue(mp);
                gridMisiones.Rows.Add(mp.Name, $"({mp.Coords.X:F1}, {mp.Coords.Y:F1})", mp.Status);
            }
            LogEvent("RUN_START", $"Circuito {currentCircuitName}, repetición {repetitionInCircuit}/{cfg.RepetitionsPerCircuit}, inicio desde ({startPos.X:F3},{startPos.Y:F3})");
            LoadNextWaypoint();
        }

        // Distancia en línea recta y proxy de cambio de rumbo (|Δψ|, definidos igual
        // que en el paper: bearing entre waypoints consecutivos, fijando Δψ del primer
        // segmento en 0) para cada segmento del circuito activo. El primer segmento usa
        // startPos (la posición real del robot al arrancar); el resto usa el waypoint
        // anterior de la lista. Se precalcula una vez por circuito, no por tick.
        private void ComputeSegmentGeometry(PointF startPos, List<PointF> targets, out float[] straightDist, out float[] headingProxyDeg)
        {
            int n = targets.Count;
            straightDist = new float[n];
            headingProxyDeg = new float[n];
            float[] bearing = new float[n];

            for (int i = 0; i < n; i++)
            {
                PointF prev = (i == 0) ? startPos : targets[i - 1];
                PointF cur = targets[i];
                float dx = cur.X - prev.X;
                float dy = cur.Y - prev.Y;
                straightDist[i] = (float)Math.Sqrt(dx * dx + dy * dy);
                bearing[i] = (float)Math.Atan2(dx, dy);
            }

            if (n > 0) headingProxyDeg[0] = 0f;
            for (int i = 1; i < n; i++)
            {
                float d = WrapRadians(bearing[i] - bearing[i - 1]);
                headingProxyDeg[i] = Math.Abs(d) * (180f / (float)Math.PI);
            }
        }

        private static float WrapRadians(float rad)
        {
            while (rad > Math.PI) rad -= 2f * (float)Math.PI;
            while (rad < -Math.PI) rad += 2f * (float)Math.PI;
            return rad;
        }

        private void LoadNextWaypoint()
        {
            if (MissionQueue.Count > 0)
            {
                if (CurrentMission != null)
                {
                    UpdateGridStatus(CurrentMission.Name, "Completado");
                    WriteSegmentSummaryRow();
                }
                CurrentMission = MissionQueue.Dequeue();
                segmentIndexInCircuit++;
                HasGoal = true;
                UpdateGridStatus(CurrentMission.Name, ">> ACTIVO");
                if (ra != null) ra.SetGoalVisual(CurrentMission.Coords.X, CurrentMission.Coords.Y);
                ListPoints = new List<Point>();
                lastPathCalc = DateTime.MinValue;
                stuckTimer = DateTime.Now;
                pathFailCount = 0;
                ResetSegmentAccumulators();
                //textBox8.Text = CurrentMission.Coords.X.ToString("F2");
                //textBox9.Text = CurrentMission.Coords.Y.ToString("F2");
            }
            else
            {
                if (CurrentMission != null)
                {
                    UpdateGridStatus(CurrentMission.Name, "Completado");
                    WriteSegmentSummaryRow();
                }
                AdvanceRunOrFinish();
            }
        }

        // Avanza a la siguiente repetición del circuito activo, o al siguiente
        // circuito de cfg.CircuitSequence, o termina el experimento si ya se
        // agotó la secuencia completa (circuitos × repeticiones).
        private void AdvanceRunOrFinish()
        {
            repetitionInCircuit++;
            if (repetitionInCircuit > cfg.RepetitionsPerCircuit)
            {
                repetitionInCircuit = 1;
                circuitSeqIndex++;
            }

            if (circuitSeqIndex < cfg.CircuitSequence.Count)
            {
                currentCircuitName = cfg.CircuitSequence[circuitSeqIndex];
                currentRun++;
                CargarYArrancarMision();
            }
            else
            {
                LogEvent("EXPERIMENT_FINISHED", "Todas las repeticiones y circuitos completados");
                HasGoal = false; Detener = 1; isLogging = false;
                lblStatus.Text = "FIN EXPERIMENTO";
                lblStatus.ForeColor = Color.Purple;
                if (logger != null) { logger.Close(); logger = null; }
                if (segmentLogger != null) { segmentLogger.Close(); segmentLogger = null; }
                if (eventsLogger != null) { eventsLogger.Close(); eventsLogger = null; }
                if (ra != null && RobDrive != null) { RobDrive.SetDifferential(0, 0); ra.Send(RobDrive); }
                MessageBox.Show("Experimento finalizado. Datos guardados en:\n" + currentRunFolder);
            }
        }

        private void ResetSegmentAccumulators()
        {
            timeRotating = 0; timeTranslating = 0; timeConverging = 0; timeReplanning = 0;
            executedPathLength = 0; integratedAbsYawDeg = 0;
            segmentMinClearanceRadius = float.MaxValue;
            lastPlannedPathCost = 0;
            havePlannedPathForSegment = false;
            segmentMinDistToGoal = float.MaxValue;
            haveLastSample = false;
            lastTickMs = (isLogging && stopwatch != null) ? stopwatch.ElapsedMilliseconds : 0;
        }

        // Escribe una fila de segment_summary.csv con las cantidades físicas
        // realmente ejecutadas (distancia recorrida, yaw integrado, holgura mínima)
        // y los tiempos instrumentados por fase, para contrastar contra lo que el
        // modelo de regresión del paper atribuye a cada término (puntos #1, #2, #3,
        // #4, #9 de los revisores).
        private void WriteSegmentSummaryRow(string outcome = "Completed")
        {
            if (segmentLogger == null || cfg == null) return;

            int idx = segmentIndexInCircuit - 1;
            float straightDist = (segStraightDist != null && idx >= 0 && idx < segStraightDist.Length) ? segStraightDist[idx] : 0f;
            float headingProxy = (segHeadingProxyDeg != null && idx >= 0 && idx < segHeadingProxyDeg.Length) ? segHeadingProxyDeg[idx] : 0f;
            float clearance = segmentMinClearanceRadius == float.MaxValue ? 0f : segmentMinClearanceRadius;
            double total = timeRotating + timeTranslating + timeConverging + timeReplanning;

            var fields = new string[]
            {
                currentRun.ToString(CultureInfo.InvariantCulture),
                currentCircuitName,
                "S" + segmentIndexInCircuit.ToString(CultureInfo.InvariantCulture),
                cfg.ControlMode.ToString(),
                cfg.PlannerHeuristic.ToString(),
                cfg.ArrivalTolerance.ToString("F3", CultureInfo.InvariantCulture),
                straightDist.ToString("F3", CultureInfo.InvariantCulture),
                (lastPlannedPathCost * 0.1f).ToString("F3", CultureInfo.InvariantCulture),
                executedPathLength.ToString("F3", CultureInfo.InvariantCulture),
                headingProxy.ToString("F1", CultureInfo.InvariantCulture),
                integratedAbsYawDeg.ToString("F1", CultureInfo.InvariantCulture),
                clearance.ToString("F0", CultureInfo.InvariantCulture),
                timeRotating.ToString("F3", CultureInfo.InvariantCulture),
                timeTranslating.ToString("F3", CultureInfo.InvariantCulture),
                timeConverging.ToString("F3", CultureInfo.InvariantCulture),
                timeReplanning.ToString("F3", CultureInfo.InvariantCulture),
                total.ToString("F3", CultureInfo.InvariantCulture),
                outcome
            };
            segmentLogger.WriteLine(string.Join(",", fields));
            segmentLogger.Flush();
        }

        private void UpdateGridStatus(string name, string status)
        {
            foreach (DataGridViewRow row in gridMisiones.Rows)
            {
                if (row.Cells[0].Value.ToString() == name)
                {
                    row.Cells[2].Value = status;
                    if (status.Contains("ACTIVO")) row.DefaultCellStyle.BackColor = Color.Yellow;
                    else if (status == "Completado") row.DefaultCellStyle.BackColor = Color.LightGreen;
                    break;
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            // Si había un segmento en curso, dejar constancia de que se cortó a mano
            // (p.ej. tras una colisión) en vez de perder ese tramo de datos en silencio.
            if (HasGoal && CurrentMission != null)
            {
                LogEvent("RUN_STOPPED_MANUAL", "Detenido manualmente durante segmento S" + segmentIndexInCircuit);
                WriteSegmentSummaryRow("Aborted");
            }
            Enviar = 0; Detener = 1; HasGoal = false; isLogging = false;
            lblStatus.Text = "DETENIDO"; lblStatus.ForeColor = Color.Red;
            if (logger != null) { logger.Close(); logger = null; }
            if (segmentLogger != null) { segmentLogger.Close(); segmentLogger = null; }
            if (eventsLogger != null) { eventsLogger.Close(); eventsLogger = null; }
            if (ra != null && RobDrive != null) { RobDrive.SetDifferential(0, 0); ra.Send(RobDrive); }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                if (ra is VrepAdapter)
                {
                    var vrep = ra as VrepAdapter;
                    string Lidar = VRepFunctions.GetStringSignal(vrep.clientID, "Lidar");
                    string RobPos = VRepFunctions.GetStringSignal(vrep.clientID, "RobPos");
                    if (Lidar != null) vrep.ReceiveLedData(Lidar);
                    if (RobPos != null) vrep.ReceiveOdomData(RobPos);
                }

                if (ra != null && ra.RobotOdomData != null)
                {
                    lblPosX.Text = $"X: {ra.RobotOdomData[0]:F2}";
                    lblPosY.Text = $"Y: {ra.RobotOdomData[1]:F2}";
                }

                if (Enviar == 1 && Detener == 0 && RobDrive != null && ra != null && SQ != null && HasGoal)
                {
                    if (cfg == null) cfg = ExperimentConfig.Default();

                    float currentRot = ra.RobotOdomData[2];
                    // NOTA: se probó relajar esta condición (forzar actualización del
                    // mapa durante el giro de búsqueda / con mapa vacío) para evitar
                    // bloqueos circulares. Revertido junto con el reset de mapa por
                    // repetición: la combinación empeoró la tasa de fallas en vez de
                    // mejorarla (ver CargarYArrancarMision). Vuelta a la condición
                    // original, que es la que rindió mejor en la práctica.
                    if (Math.Abs(currentRot - lastRot) < 0.005 && (DateTime.Now - lastMapUpdate).TotalMilliseconds > 100)
                    {
                        map.LedDataToList(ra.RobotLedData, ra.RobotOdomData);
                        map.GlobListToGraph(map.GlobalMapList, ra.RobotOdomData);
                        RedrawStaticObstacles();
                        lastMapUpdate = DateTime.Now;
                    }
                    lastRot = currentRot;

                    float dist = (float)Math.Sqrt(Math.Pow(CurrentMission.Coords.X - ra.RobotOdomData[0], 2) + Math.Pow(CurrentMission.Coords.Y - ra.RobotOdomData[1], 2));
                    if (lblDistancia != null) lblDistancia.Text = $"Dist: {dist:F2} m";

                    if (isLogging && logger != null && stopwatch != null)
                    {
                        var row = new string[]
                        {
                            currentRun.ToString(CultureInfo.InvariantCulture),
                            currentCircuitName ?? "",
                            stopwatch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                            ra.RobotOdomData[0].ToString("F3", CultureInfo.InvariantCulture),
                            ra.RobotOdomData[1].ToString("F3", CultureInfo.InvariantCulture),
                            currentRot.ToString("F4", CultureInfo.InvariantCulture),
                            CurrentMission.Coords.X.ToString("F3", CultureInfo.InvariantCulture),
                            CurrentMission.Coords.Y.ToString("F3", CultureInfo.InvariantCulture),
                            dist.ToString("F3", CultureInfo.InvariantCulture)
                        };
                        logger.WriteLine(string.Join(",", row));
                    }

                    // Instrumentación por tick: distancia realmente recorrida, yaw
                    // integrado y tiempo por fase de control (puntos #1, #2, #9 revisores).
                    long nowMs = (isLogging && stopwatch != null) ? stopwatch.ElapsedMilliseconds : 0;
                    double dtSeconds = (haveLastSample && isLogging) ? Math.Max(0.0, (nowMs - lastTickMs) / 1000.0) : 0.0;

                    var curPos = new PointF(ra.RobotOdomData[0], ra.RobotOdomData[1]);
                    if (haveLastSample)
                    {
                        float ddx = curPos.X - lastOdomPos.X;
                        float ddy = curPos.Y - lastOdomPos.Y;
                        executedPathLength += (float)Math.Sqrt(ddx * ddx + ddy * ddy);

                        float dHeadingRad = WrapRadians(currentRot - lastHeadingForYaw);
                        integratedAbsYawDeg += Math.Abs(dHeadingRad) * (180f / (float)Math.PI);
                    }
                    lastOdomPos = curPos;
                    lastHeadingForYaw = currentRot;
                    haveLastSample = true;

                    // "Rotando" solo tiene sentido físico en modo diferencial (ahí un
                    // |phi| grande fuerza un giro en el sitio sin avanzar). En modo
                    // Holonomic el robot avanza de costado/oblicuo hacia el objetivo
                    // sin alinear el rumbo primero, así que un |phi| grande mientras
                    // se traslada es el comportamiento esperado, no tiempo perdido
                    // girando — con la regla vieja, casi todo el tiempo holonómico
                    // quedaba mal etiquetado como "Rotando" en vez de "Trasladando".
                    bool searching = isCalculating || ListPoints == null || ListPoints.Count == 0;
                    bool inPlaceRotationPhase = cfg.ControlMode == ControlModeType.DifferentialEmulated
                        && Math.Abs(RobDrive.Phi) > cfg.RotationThreshold;
                    if (searching)
                    {
                        timeReplanning += dtSeconds;
                    }
                    else if (inPlaceRotationPhase)
                    {
                        timeRotating += dtSeconds;
                    }
                    else if (dist < cfg.ConvergenceBandRadius)
                    {
                        timeConverging += dtSeconds;
                    }
                    else
                    {
                        timeTranslating += dtSeconds;
                    }
                    lastTickMs = nowMs;

                    if (dist < cfg.WaypointAdvanceRadius) { LoadNextWaypoint(); return; }

                    // Progreso real hacia el objetivo (independiente de MaxPathLengthRatio,
                    // que solo atrapa UN plan puntual desproporcionado). Un alejamiento
                    // gradual — replanteo tras replanteo, cada uno luciendo razonable en
                    // el momento — nunca dispara esa verificación, pero sí se nota en que
                    // "dist" deja de mejorar. Confirmado en una corrida real: el robot
                    // viajó ~5 m en la dirección opuesta al objetivo sin que ningún plan
                    // individual pareciera absurdo.
                    if (dist < segmentMinDistToGoal) segmentMinDistToGoal = dist;

                    if ((DateTime.Now - stuckTimer).TotalSeconds > 5)
                    {
                        float move = (float)Math.Sqrt(Math.Pow(ra.RobotOdomData[0] - lastPos.X, 2) + Math.Pow(ra.RobotOdomData[1] - lastPos.Y, 2));
                        if (move < 0.1f)
                        {
                            ListPoints = null; lastPathCalc = DateTime.MinValue; lblStatus.Text = "DESBLOQUEANDO...";
                            LogEvent("STUCK_DETECTED", $"Avanzó {move:F3} m en 5 s, error actual {dist:F3} m — posible colisión/bloqueo");
                        }
                        else if (dist > segmentMinDistToGoal + cfg.WrongDirectionMarginM)
                        {
                            ListPoints = null; lastPathCalc = DateTime.MinValue; lblStatus.Text = "CORRIGIENDO RUMBO...";
                            LogEvent("MOVING_AWAY_FROM_GOAL",
                                $"Distancia actual {dist:F2} m supera en más de {cfg.WrongDirectionMarginM:F1} m el mejor progreso logrado ({segmentMinDistToGoal:F2} m)");
                            segmentMinDistToGoal = dist;
                        }
                        stuckTimer = DateTime.Now; lastPos = new PointF(ra.RobotOdomData[0], ra.RobotOdomData[1]);
                    }

                    if (!isCalculating && (DateTime.Now - lastPathCalc).TotalMilliseconds > 600)
                    {
                        RecalcularRutaAsync(); lastPathCalc = DateTime.Now;
                    }

                    if (ListPoints != null && ListPoints.Count > 0)
                    {
                        SQ.GetNextPoint(ListPoints, ra.RobotOdomData[0], ra.RobotOdomData[1], ra.RobotOdomData[2], map.Xmax, map.Ymax);
                        if (cfg.ControlMode == ControlModeType.Holonomic)
                            RobDrive.GetHolonomicDrive(ra.RobotOdomData[0], ra.RobotOdomData[1], ra.RobotOdomData[2], SQ.CurrentPointX, SQ.CurrentPointY, map.Xmax, map.Ymax);
                        else
                            RobDrive.GetDrive(ra.RobotOdomData[0], ra.RobotOdomData[1], ra.RobotOdomData[2], SQ.CurrentPointX, SQ.CurrentPointY, map.Xmax, map.Ymax);

                        if (dist < cfg.ConvergenceBandRadius)
                        {
                            RobDrive.left *= cfg.ConvergenceDampingFactor; RobDrive.right *= cfg.ConvergenceDampingFactor;
                            RobDrive.wheelFL *= cfg.ConvergenceDampingFactor; RobDrive.wheelFR *= cfg.ConvergenceDampingFactor;
                            RobDrive.wheelRL *= cfg.ConvergenceDampingFactor; RobDrive.wheelRR *= cfg.ConvergenceDampingFactor;
                        }
                        ra.Send(RobDrive);
                    }
                    else
                    {
                        if (!isCalculating) { RobDrive.SetDifferential(-0.6f, 0.6f); ra.Send(RobDrive); lblStatus.Text = "BUSCANDO RUTA..."; }
                    }
                }

                if (visualEnabled && mapImage != null && g != null)
                {
                    ActualizarMapaVisual();
                    pictureBox1.Invalidate();
                }
            }
            catch (OutOfMemoryException)
            {
                visualEnabled = false; LimpiarMemoriaGrafica(); lblStatus.Text = "ERROR MEMORIA - SOLO DATOS";
            }
            catch { }
        }

        private async void RecalcularRutaAsync()
        {
            isCalculating = true; float rx = ra.RobotOdomData[0]; float ry = ra.RobotOdomData[1];
            float[,] graphCopy;
            try { if (map.graph != null) graphCopy = (float[,])map.graph.Clone(); else { isCalculating = false; return; } }
            catch { isCalculating = false; return; }

            await Task.Run(() => {
                try
                {
                    Point startP = new Point((int)(rx * 10 + map.Xmax / 2), (int)(ry * 10 + map.Ymax / 2));
                    Point targetP = new Point((int)(CurrentMission.Coords.X * 10 + map.Xmax / 2), (int)(CurrentMission.Coords.Y * 10 + map.Ymax / 2));
                    float pathCost;
                    var newPath = SiG.FindPath(graphCopy, GetClosestValidPoint(graphCopy, startP, 15), GetClosestValidPoint(graphCopy, targetP, 20), out pathCost);
                    int clearanceRadius = SearchInGraph.GetMinClearanceRadius(newPath, graphCopy, 10);

                    // Salvaguarda de plausibilidad: si el camino planificado es
                    // desproporcionadamente más largo que la distancia en línea recta
                    // al objetivo (mapa acumulado con ruido/deriva puede generar un A*
                    // "válido" pero absurdo), se rechaza en vez de comprometerse a
                    // ejecutarlo ciegamente. Ver MaxPathLengthRatio en ExperimentConfig.
                    float straightLineNow = (float)Math.Sqrt(Math.Pow(CurrentMission.Coords.X - rx, 2) + Math.Pow(CurrentMission.Coords.Y - ry, 2));
                    float plannedMeters = pathCost * 0.1f;
                    bool implausible = newPath != null && newPath.Count > 0 && straightLineNow > 0.5f
                        && plannedMeters > straightLineNow * cfg.MaxPathLengthRatio;

                    this.Invoke(new Action(() => {
                        if (implausible)
                        {
                            pathFailCount++;
                            LogEvent("IMPLAUSIBLE_PATH_REJECTED",
                                $"Ruta de {plannedMeters:F1} m rechazada para un objetivo a {straightLineNow:F1} m en línea recta (ratio {plannedMeters / straightLineNow:F1}x > {cfg.MaxPathLengthRatio:F1}x)");
                            if (pathFailCount > 3) ListPoints = null;
                        }
                        else if (newPath != null && newPath.Count > 0)
                        {
                            ListPoints = newPath; pathFailCount = 0;
                            // Solo el PRIMER camino exitoso del segmento cuenta como "ruta
                            // planificada" (de waypoint a waypoint); los replanteos
                            // posteriores, ya cerca de la meta, son solo el tramo final
                            // restante y no deben pisar ese valor.
                            if (!havePlannedPathForSegment)
                            {
                                lastPlannedPathCost = pathCost;
                                havePlannedPathForSegment = true;
                            }
                            if (clearanceRadius < segmentMinClearanceRadius) segmentMinClearanceRadius = clearanceRadius;
                        }
                        else
                        {
                            pathFailCount++;
                            if (pathFailCount > 3)
                            {
                                ListPoints = null;
                                LogEvent("PATH_NOT_FOUND", "Más de 3 intentos consecutivos sin ruta válida hacia el objetivo");
                            }
                        }
                    }));
                }
                catch { }
                finally { isCalculating = false; }
            });
        }

        private Point GetClosestValidPoint(float[,] graph, Point p, int radius)
        {
            int maxX = graph.GetLength(0); int maxY = graph.GetLength(1);
            if (p.X >= 0 && p.X < maxX && p.Y >= 0 && p.Y < maxY && graph[p.X, p.Y] < 2) return p;
            for (int r = 1; r <= radius; r++) for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++)
                    {
                        int nx = p.X + x, ny = p.Y + y;
                        if (nx >= 0 && nx < maxX && ny >= 0 && ny < maxY && graph[nx, ny] < 2) return new Point(nx, ny);
                    }
            return p;
        }

        private void RedrawStaticObstacles()
        {
            if (!visualEnabled || staticMapLayer == null || map.GlobalMapList == null) return;
            int limitX = staticMapLayer.Width; int limitY = staticMapLayer.Height;
            try
            {
                using (SolidBrush blueBrush = new SolidBrush(Color.Blue))
                {
                    if (map.GlobalMapList.Count < lastDrawnIndex) { gStatic.Clear(Color.White); lastDrawnIndex = 0; }
                    for (int i = lastDrawnIndex; i < map.GlobalMapList.Count; i++)
                    {
                        var obs = map.GlobalMapList[i];
                        int gx = (int)(obs.X * 10 + map.Xmax / 2); int gy = (int)(obs.Y * 10 + map.Ymax / 2);
                        int vizX = gy * currentCellSize; int vizY = gx * currentCellSize;
                        if (vizX >= 0 && vizX < limitX - currentCellSize && vizY >= 0 && vizY < limitY - currentCellSize)
                        {
                            gStatic.FillRectangle(blueBrush, vizX, vizY, currentCellSize, currentCellSize);
                        }
                    }
                    lastDrawnIndex = map.GlobalMapList.Count;
                }
            }
            catch (OutOfMemoryException) { visualEnabled = false; LimpiarMemoriaGrafica(); }
        }

        private void ActualizarMapaVisual()
        {
            if (!visualEnabled || staticMapLayer == null || mapImage == null || g == null) return;
            try
            {
                g.DrawImageUnscaled(staticMapLayer, 0, 0);
                int limitX = mapImage.Width; int limitY = mapImage.Height;
                if (ListPoints != null)
                {
                    using (SolidBrush redBrush = new SolidBrush(Color.Red))
                    {
                        int count = ListPoints.Count;
                        for (int i = 0; i < count; i++)
                        {
                            Point p = ListPoints[i];
                            int px = p.Y * currentCellSize; int py = p.X * currentCellSize;
                            if (px >= 0 && px < limitX - currentCellSize && py >= 0 && py < limitY - currentCellSize)
                                g.FillRectangle(redBrush, px, py, currentCellSize, currentCellSize);
                        }
                    }
                }
                if (ra != null && ra.RobotOdomData != null)
                {
                    Point start = new Point((int)(ra.RobotOdomData[0] * 10 + map.Xmax / 2), (int)(ra.RobotOdomData[1] * 10 + map.Ymax / 2));
                    int sz = currentCellSize * 3; if (sz < 5) sz = 5;
                    int xRob = (start.Y * currentCellSize) - sz / 2; int yRob = (start.X * currentCellSize) - sz / 2;
                    if (xRob >= 0 && xRob < limitX - sz && yRob >= 0 && yRob < limitY - sz)
                    {
                        using (Pen pChoco = new Pen(Color.Chocolate, 2)) using (Pen pBlack = new Pen(Color.Black, 2))
                        {
                            g.DrawEllipse(pChoco, xRob, yRob, sz, sz);
                            float ang = ra.RobotOdomData[2];
                            g.DrawLine(pBlack, xRob + sz / 2, yRob + sz / 2, xRob + sz / 2 + (float)(Math.Sin(ang) * sz), yRob + sz / 2 + (float)(Math.Cos(ang) * sz));
                        }
                    }
                }
            }
            catch (Exception) { visualEnabled = false; LimpiarMemoriaGrafica(); }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ra != null) ra.Deactivate();
            if (logger != null) logger.Close();
            if (segmentLogger != null) segmentLogger.Close();
            if (eventsLogger != null) eventsLogger.Close();
            LimpiarMemoriaGrafica();
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e) { }
        private void Form1_Load(object sender, EventArgs e) { }
        private void textBox8_TextChanged(object sender, EventArgs e) { }
        private void textBox9_TextChanged(object sender, EventArgs e) { }
        private void label11_Click(object sender, EventArgs e) { }
        private void label5_Click(object sender, EventArgs e) { }
        private void textBox5_TextChanged(object sender, EventArgs e) { }
        private void textBox3_TextChanged(object sender, EventArgs e) { }
        private void pictureBox1_Click(object sender, EventArgs e) { }
    }
}