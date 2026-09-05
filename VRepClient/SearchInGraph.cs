//búsqueda de gráficos (A*)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Collections.ObjectModel;

namespace VRepClient
{
    public class PathNode
    {
        public Point Position { get; set; }//Coordenadas de un punto en el mapa.
        public float PathLengthFromStart { get; set; }//Longitud del camino desde el inicio hasta el punto (G)
        public PathNode CameFrom { get; set; }//El punto desde donde llegaste a este punto.
        public float HeuristicEstimatePathLength { get; set; }//Distancia aproximada del punto al objetivo (H)

        public float EstimateFullPathLength
        {
            get
            {
                return this.PathLengthFromStart + this.HeuristicEstimatePathLength;
            }
        }
    }

    public class SearchInGraph
    {
        // Heurística activa, inyectada desde ExperimentConfig. Manhattan es la
        // original (no admisible en grilla de 8 conexiones con costo diagonal 1.4);
        // Octile sí es admisible/consistente para ese costo y sirve como línea base
        // de planificador admisible (punto #6 de los revisores).
        public PlannerHeuristicType Heuristic = PlannerHeuristicType.Manhattan;

        public List<Point> FindPath(float[,] field, Point start, Point goal, out float pathCost) //eliminado estático
        {   //PASO 1
            pathCost = 0f;
            var closedSet = new Collection<PathNode>();
            var openSet = new Collection<PathNode>();
            //PASO 2
            PathNode startNode = new PathNode()
            {
                Position = start,
                CameFrom = null,
                PathLengthFromStart = 0,
                HeuristicEstimatePathLength = GetHeuristicPathLenght(start, goal)
            };

            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                //PASO 3
                var currentNode = openSet.OrderBy(node => node.EstimateFullPathLength).First();
                //PASO 4
                if (currentNode.Position == goal)
                {
                    pathCost = currentNode.PathLengthFromStart;
                    return GetPathForNode(currentNode);
                }
                //PASO 5
                openSet.Remove(currentNode);
                closedSet.Add(currentNode);
                //PASO 6
                foreach (var neighbourNode in GetNeighbours(currentNode, goal, field))
                {
                    //PASO 7
                    if (closedSet.Count(node => node.Position == neighbourNode.Position) > 0)
                        continue;
                    var openNode = openSet.FirstOrDefault(node => node.Position == neighbourNode.Position);
                    //PASO 8
                    if (openNode == null)
                        openSet.Add(neighbourNode);
                    else
                        if (openNode.PathLengthFromStart > neighbourNode.PathLengthFromStart)
                    {
                        //PASO 9
                        openNode.CameFrom = currentNode;
                        openNode.PathLengthFromStart = neighbourNode.PathLengthFromStart;
                    }
                }
            }
            //PASO 10
            return null;
        }

        private static float GetDistanceBetweenNeighbours(float weight)//Función de distancia de X a Y (peso int)
        {
            return weight;//aquí necesitamos sumar la permeabilidad de la celda, en este momento la distancia siempre es igual a 1
        }

        private float GetHeuristicPathLenght(Point from, Point to)// función para la estimación de la distancia aproximada al objetivo
        {
            int dx = Math.Abs(from.X - to.X);
            int dy = Math.Abs(from.Y - to.Y);
            if (Heuristic == PlannerHeuristicType.Octile)
            {
                // Admisible/consistente para costo axial=1.0 y diagonal=1.4 (Section 2.1).
                return Math.Max(dx, dy) + 0.4f * Math.Min(dx, dy);
            }
            return dx + dy; // Manhattan (comportamiento original, no admisible en 8 conexiones)
        }

        private Collection<PathNode> GetNeighbours(PathNode pathNode, Point goal, float[,] field)
        {
            var result = new Collection<PathNode>();
            //Los puntos vecinos son celdas adyacentes por un lado.
            Point[] neighbourPoints = new Point[8];
            neighbourPoints[0] = new Point(pathNode.Position.X + 1, pathNode.Position.Y);
            neighbourPoints[1] = new Point(pathNode.Position.X - 1, pathNode.Position.Y);
            neighbourPoints[2] = new Point(pathNode.Position.X, pathNode.Position.Y + 1);
            neighbourPoints[3] = new Point(pathNode.Position.X, pathNode.Position.Y - 1);
            neighbourPoints[4] = new Point(pathNode.Position.X + 1, pathNode.Position.Y + 1);
            neighbourPoints[5] = new Point(pathNode.Position.X - 1, pathNode.Position.Y - 1);
            neighbourPoints[6] = new Point(pathNode.Position.X - 1, pathNode.Position.Y + 1);
            neighbourPoints[7] = new Point(pathNode.Position.X + 1, pathNode.Position.Y - 1);

            foreach (var point in neighbourPoints) //comprobar si el mapa ha ido más allá de los límites
            {
                if (point.X < 0 || point.X >= field.GetLength(0))
                    continue;
                if (point.Y < 0 || point.Y >= field.GetLength(1))
                    continue;
                //Comprueba que puedes caminar alrededor de la jaula.
                //revisa las cinco celdas más cercanas
                int freeNode = 0;

                for (int i = -3; i < 4; i++)
                {
                    for (int k = -3; k < 4; k++)
                    {
                        if (point.X + i > 0 && point.X + i < field.GetLength(0))
                        {
                            if (point.Y + k > 0 && point.Y + k < field.GetLength(1))
                            {
                                if (field[point.X + i, point.Y + k] == 1)
                                {
                                    freeNode++;
                                }
                            }
                        }
                    }
                }

                float weight;

                if (pathNode.Position.X != point.X && pathNode.Position.Y != point.Y)//los desplazamientos diagonales cuestan 1,4 y los rectos cuestan 1
                    weight = 1.4f;
                else
                    weight = 1;

                if ((field[point.X, point.Y] < 2) && freeNode == 49)
                {
                    //Complete los datos para el punto de ruta.
                    var neighbourNode = new PathNode()
                    {
                        Position = point,
                        CameFrom = pathNode,
                        PathLengthFromStart = pathNode.PathLengthFromStart + GetDistanceBetweenNeighbours(weight),
                        HeuristicEstimatePathLength = GetHeuristicPathLenght(point, goal)
                    };

                    result.Add(neighbourNode);
                }
            }
            return result;
        }

        private static List<Point> GetPathForNode(PathNode pathNode)
        {
            var result = new List<Point>();
            var currentNode = pathNode;

            while (currentNode != null)
            {
                result.Add(currentNode.Position);
                currentNode = currentNode.CameFrom;
            }

            result.Reverse();
            return result;
        }

        // Radio de holgura (clearance) mínimo a lo largo de un camino ya calculado:
        // para cada nodo, el mayor R tal que una ventana (2R+1)x(2R+1) centrada en él
        // está completamente libre de obstáculos. El planificador ya exige R>=3 para
        // admitir cualquier nodo (Section 2.1); este valor cuantifica cuánto margen
        // real hay por encima de ese mínimo a lo largo del camino ejecutado — el
        // covariable de "clearance slack" que la Discusión del paper propone agregar
        // al modelo (puntos #3 y #8 de los revisores).
        public static int GetMinClearanceRadius(List<Point> path, float[,] field, int maxRadius)
        {
            if (path == null || path.Count == 0) return 0;
            int minRadius = maxRadius;
            foreach (var p in path)
            {
                int r = GetClearanceRadiusAt(p, field, maxRadius);
                if (r < minRadius) minRadius = r;
            }
            return minRadius;
        }

        private static int GetClearanceRadiusAt(Point p, float[,] field, int maxRadius)
        {
            int achieved = 0;
            for (int r = 3; r <= maxRadius; r++)
            {
                bool free = true;
                for (int i = -r; i <= r && free; i++)
                {
                    for (int k = -r; k <= r && free; k++)
                    {
                        int xi = p.X + i, yk = p.Y + k;
                        if (xi < 0 || xi >= field.GetLength(0) || yk < 0 || yk >= field.GetLength(1))
                        {
                            free = false;
                            break;
                        }
                        if (field[xi, yk] != 1)
                        {
                            free = false;
                            break;
                        }
                    }
                }
                if (!free) break;
                achieved = r;
            }
            return achieved;
        }
    }
}
