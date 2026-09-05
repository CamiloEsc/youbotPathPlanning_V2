using System;
using System.Collections.Generic;
using System.Drawing;

namespace VRepClient
{
    public class SequencePoints
    {
        public float CurrentPointX = 0;
        public float CurrentPointY = 0;
        public float GoalPointX;
        public float GoalPointY;
        /*public float GoalPointX2;
        public float GoalPointY2;
        public float GoalPointX3;
        public float GoalPointY3;*/

        public bool KeyForSearchInGraph = true;

        // Antes hardcodeados (3 celdas / salto de 4), ahora inyectados desde ExperimentConfig.
        public int LookaheadCells = 3;
        public int LookaheadAdvance = 4;

        public void GetNextPoint(List<Point> ListPoints, float RobX, float RobY, float RobA, int Xmax, int Ymax)
        {
            int RobLocX = (int)(RobX * 10) + Xmax / 2; //la posición del robot en las celdas del gráfico
            int RobLocY = (int)(RobY * 10) + Ymax / 2;

            for (int i = 1; i < ListPoints.Count - LookaheadAdvance; i++)
            {
                if (Math.Abs(ListPoints[i].X - RobLocX) < LookaheadCells && Math.Abs(ListPoints[i].Y - RobLocY) < LookaheadCells)
                {
                    CurrentPointX = ListPoints[i + LookaheadAdvance].X;
                    CurrentPointY = ListPoints[i + LookaheadAdvance].Y;
                    KeyForSearchInGraph = true;
                }
            }
        }
    }
}
