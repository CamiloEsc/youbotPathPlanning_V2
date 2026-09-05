using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VRepClient
{
    // Modo de control: réplica del comportamiento actual (diferencial-emulado sobre
    // chasis mecanum) o base holonómica real con comando lateral independiente.
    public enum ControlModeType
    {
        DifferentialEmulated,
        Holonomic
    }

    // Heurística del planificador A*. Manhattan es la actual (no admisible en grilla
    // de 8 conexiones con costo diagonal 1.4). Octile sí es admisible para ese costo.
    public enum PlannerHeuristicType
    {
        Manhattan,
        Octile
    }

    // Configuración del experimento, cargada desde un archivo de texto plano
    // (experiment.cfg) para poder correr barridos de parámetros y circuitos
    // variados sin recompilar, y para dejar trazabilidad exacta de cada corrida
    // (se vuelca íntegra en run_metadata_*.txt vía ToMetadataString()).
    public class ExperimentConfig
    {
        public ControlModeType ControlMode = ControlModeType.DifferentialEmulated;
        public PlannerHeuristicType PlannerHeuristic = PlannerHeuristicType.Manhattan;

        // m: distancia de frenado contra el punto perseguido (Drive.cs)
        public float ArrivalTolerance = 0.03f;
        // m: distancia contra la meta final que dispara el avance al siguiente waypoint (Form1.cs)
        public float WaypointAdvanceRadius = 0.35f;
        // rad: |phi| por encima del cual el modo diferencial fuerza giro en el sitio
        public float RotationThreshold = 0.4f;
        // m: por debajo de esta distancia a la meta se aplica el amortiguamiento de convergencia
        public float ConvergenceBandRadius = 1.0f;
        public float ConvergenceDampingFactor = 0.6f;

        public int LookaheadCells = 3;
        public int LookaheadAdvance = 4;

        // Ganancia final aplicada a los comandos de rueda normalizados en RobotAdapter.Send.
        public float WheelGain = -5.0f;

        // Salvaguarda de plausibilidad: si el A* devuelve un camino cuya longitud
        // supera este múltiplo de la distancia en línea recta al objetivo, se
        // rechaza en vez de ejecutarlo ciegamente. Se agregó tras observar al
        // robot comprometerse con desvíos de >15 m para segmentos de ~10 m en
        // línea recta (mapa acumulado con ruido/deriva entre repeticiones),
        // terminando encajado contra un límite del mapa lejos de la ruta real.
        public float MaxPathLengthRatio = 3.0f;

        // Salvaguarda complementaria: si la distancia al objetivo empeora más
        // de esta cantidad (m) respecto al mejor progreso logrado en el
        // segmento, se fuerza un replanteo. A diferencia de MaxPathLengthRatio
        // (que solo detecta UN plan puntual desproporcionado), esto detecta un
        // alejamiento GRADUAL — replanteo tras replanteo cada uno luce
        // razonable, pero el robot termina a 10-15 m del objetivo. Confirmado
        // en una corrida real: viajó de -4.8 a -10.0 en Y sin que ningún plan
        // individual disparara MaxPathLengthRatio.
        public float WrongDirectionMarginM = 2.0f;

        // Multiplicadores de signo (+1 o -1) para calibrar el modo Holonomic
        // sin recompilar. La primera prueba real mostró al robot yendo de
        // costado hacia atrás en vez de hacia el objetivo — probar
        // combinaciones de estos 3 signos (uno por vez, reiniciando la app)
        // hasta que ande derecho. Ver comentario en Drive.GetHolonomicDrive.
        public float HolonomicVxSign = 1.0f;
        public float HolonomicVySign = 1.0f;
        public float HolonomicOmegaSign = 1.0f;

        public int RepetitionsPerCircuit = 10;
        public List<string> CircuitSequence = new List<string> { "PaperTable1" };
        public Dictionary<string, List<PointF>> Circuits = new Dictionary<string, List<PointF>>();

        public string SourcePath = "(valores por defecto, no se encontró experiment.cfg)";

        public static ExperimentConfig Default()
        {
            var cfg = new ExperimentConfig();

            // Circuito de la Tabla 1 del paper (coordenadas exactas reportadas).
            cfg.Circuits["PaperTable1"] = new List<PointF>
            {
                new PointF(2.00f, 5.00f),
                new PointF(-2.00f, 5.00f),
                new PointF(0.00f, 4.00f),
                new PointF(0.00f, 1.00f),
                new PointF(0.00f, -1.00f),
                new PointF(2.00f, -1.00f),
                new PointF(0.00f, -2.00f),
                new PointF(2.00f, -4.00f),
                new PointF(-2.00f, -5.00f),
                new PointF(-2.00f, 5.00f)
            };

            // Circuito que ya estaba hardcodeado en Form1.cs antes de esta configuración
            // (distinto del publicado); se conserva como segundo circuito disponible.
            cfg.Circuits["CurrentCode"] = new List<PointF>
            {
                new PointF(-2.5f, 5.0f),
                new PointF(2.2f, 5.0f),
                new PointF(0.0f, 4.0f),
                new PointF(2.0f, -1.5f),
                new PointF(2.0f, -4.0f),
                new PointF(-2.0f, 5.0f),
                new PointF(2.0f, 5.0f),
                new PointF(0.0f, 4.0f),
                new PointF(-2.0f, -1.0f),
                new PointF(-2.0f, -5.0f)
            };

            return cfg;
        }

        public static ExperimentConfig Load(string path)
        {
            var cfg = Default();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return cfg;

            cfg.SourcePath = path;
            bool circuitSequenceSpecified = false;
            string currentCircuit = null;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string section = line.Substring(1, line.Length - 2).Trim();
                    if (section.StartsWith("Circuit:", StringComparison.OrdinalIgnoreCase))
                    {
                        currentCircuit = section.Substring("Circuit:".Length).Trim();
                        cfg.Circuits[currentCircuit] = new List<PointF>();
                    }
                    else
                    {
                        currentCircuit = null;
                    }
                    continue;
                }

                if (currentCircuit != null)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        float x, y;
                        bool okX = float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                        bool okY = float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                        if (okX && okY)
                            cfg.Circuits[currentCircuit].Add(new PointF(x, y));
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                try
                {
                    if (key == "CircuitSequence") circuitSequenceSpecified = true;
                    ApplyKey(cfg, key, value);
                }
                catch
                {
                    // clave/valor malformado: se ignora y se conserva el valor por defecto
                }
            }

            if (!circuitSequenceSpecified)
                cfg.CircuitSequence = new List<string> { "PaperTable1" };

            return cfg;
        }

        private static string StripComment(string line)
        {
            int idx = line.IndexOf('#');
            return idx >= 0 ? line.Substring(0, idx) : line;
        }

        private static void ApplyKey(ExperimentConfig cfg, string key, string value)
        {
            switch (key)
            {
                case "ControlMode":
                    cfg.ControlMode = (ControlModeType)Enum.Parse(typeof(ControlModeType), value, true);
                    break;
                case "PlannerHeuristic":
                    cfg.PlannerHeuristic = (PlannerHeuristicType)Enum.Parse(typeof(PlannerHeuristicType), value, true);
                    break;
                case "ArrivalTolerance":
                    cfg.ArrivalTolerance = ParseFloat(value, cfg.ArrivalTolerance);
                    break;
                case "WaypointAdvanceRadius":
                    cfg.WaypointAdvanceRadius = ParseFloat(value, cfg.WaypointAdvanceRadius);
                    break;
                case "RotationThreshold":
                    cfg.RotationThreshold = ParseFloat(value, cfg.RotationThreshold);
                    break;
                case "ConvergenceBandRadius":
                    cfg.ConvergenceBandRadius = ParseFloat(value, cfg.ConvergenceBandRadius);
                    break;
                case "ConvergenceDampingFactor":
                    cfg.ConvergenceDampingFactor = ParseFloat(value, cfg.ConvergenceDampingFactor);
                    break;
                case "LookaheadCells":
                    cfg.LookaheadCells = ParseInt(value, cfg.LookaheadCells);
                    break;
                case "LookaheadAdvance":
                    cfg.LookaheadAdvance = ParseInt(value, cfg.LookaheadAdvance);
                    break;
                case "WheelGain":
                    cfg.WheelGain = ParseFloat(value, cfg.WheelGain);
                    break;
                case "MaxPathLengthRatio":
                    cfg.MaxPathLengthRatio = ParseFloat(value, cfg.MaxPathLengthRatio);
                    break;
                case "WrongDirectionMarginM":
                    cfg.WrongDirectionMarginM = ParseFloat(value, cfg.WrongDirectionMarginM);
                    break;
                case "HolonomicVxSign":
                    cfg.HolonomicVxSign = ParseFloat(value, cfg.HolonomicVxSign);
                    break;
                case "HolonomicVySign":
                    cfg.HolonomicVySign = ParseFloat(value, cfg.HolonomicVySign);
                    break;
                case "HolonomicOmegaSign":
                    cfg.HolonomicOmegaSign = ParseFloat(value, cfg.HolonomicOmegaSign);
                    break;
                case "RepetitionsPerCircuit":
                    cfg.RepetitionsPerCircuit = ParseInt(value, cfg.RepetitionsPerCircuit);
                    break;
                case "CircuitSequence":
                    cfg.CircuitSequence = value.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToList();
                    break;
                default:
                    break; // clave desconocida: se ignora
            }
        }

        private static float ParseFloat(string value, float fallback)
        {
            float result;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        // Volcado íntegro de la configuración activa, para dejar registro exacto de
        // los parámetros usados en cada corrida (reproducibilidad, punto #10 de revisores).
        public string ToMetadataString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Configuración de experimento cargada desde: " + SourcePath);
            sb.AppendLine("ControlMode=" + ControlMode);
            sb.AppendLine("PlannerHeuristic=" + PlannerHeuristic);
            sb.AppendLine("ArrivalTolerance=" + ArrivalTolerance.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WaypointAdvanceRadius=" + WaypointAdvanceRadius.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RotationThreshold=" + RotationThreshold.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ConvergenceBandRadius=" + ConvergenceBandRadius.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ConvergenceDampingFactor=" + ConvergenceDampingFactor.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("LookaheadCells=" + LookaheadCells.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("LookaheadAdvance=" + LookaheadAdvance.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WheelGain=" + WheelGain.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("MaxPathLengthRatio=" + MaxPathLengthRatio.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WrongDirectionMarginM=" + WrongDirectionMarginM.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("HolonomicVxSign=" + HolonomicVxSign.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("HolonomicVySign=" + HolonomicVySign.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("HolonomicOmegaSign=" + HolonomicOmegaSign.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RepetitionsPerCircuit=" + RepetitionsPerCircuit.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("CircuitSequence=" + string.Join(",", CircuitSequence));
            foreach (var kv in Circuits)
            {
                sb.AppendLine("[Circuit:" + kv.Key + "] (" + kv.Value.Count.ToString(CultureInfo.InvariantCulture) + " waypoints)");
                foreach (var p in kv.Value)
                {
                    sb.AppendLine("  " + p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }
    }
}
