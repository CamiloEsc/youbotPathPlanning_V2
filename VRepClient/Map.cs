using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace VRepClient
{
    public class ObstaclesPoint
    {
        public float X;
        public float Y;
        public float weight = 2f;
    }

    public class Map
    {
        public List<ObstaclesPoint> GlobalMapList = new List<ObstaclesPoint>();
        public float[] RobOdData; //solo para mostrar imágenes con un robot en el formulario
        public bool invalidateform = false;

        public void LedDataToList(float[] LedData, float[] OdomData)
        {
            RobOdData = new float[OdomData.Length];
            RobOdData[0] = OdomData[0]; RobOdData[1] = OdomData[1]; RobOdData[2] = OdomData[2];//línea solo para salida móvil
            float[,] LedDataMass = new float[LedData.Length, 2];//matriz con coordenadas de puntos relativos al robot // eliminar comentarios
            float A = 180f / LedData.Length;//ángulo entre datos ledar
            float x; // ángulo entre el cateto y la hipotenusa
            float c; //longitud de la hipotenusa

            for (int i = 0; i < LedData.Length; i++)
            {
                c = LedData[i];
                x = i * A;
                x = x * 0.0174533f - OdomData[2];

                if (OdomData[2] > 1.571 || OdomData[2] < 1.570)
                {
                    float LedRX = 0.344f * (float)Math.Cos(OdomData[2]);
                    float LedRY = 0.344f * (float)Math.Sin(OdomData[2]);
                    LedDataMass[i, 0] = ((float)Math.Cos(x) * c) + OdomData[0] + LedRY;//valor del eje X
                    LedDataMass[i, 1] = ((float)Math.Sin(x) * c) + OdomData[1] + LedRX;// valor del eje Y 
                }
            }

            GlobalMapList.Add(new ObstaclesPoint { X = 0f, Y = 0f, weight = 1 });//Establecí dos puntos predeterminados en ambos lados del robot. 

            float Xpel;
            float Ypel;
            float DistBetweenPoints = 0;

            for (int g = 0; g < LedData.Length; g++)
            {
                float radius = 0;
                float h = 4;//recordar el punto más corto de un bucle

                for (int i = 0; i < GlobalMapList.Count; i++)
                {
                    Xpel = LedDataMass[g, 0] - GlobalMapList[i].X;
                    Ypel = LedDataMass[g, 1] - GlobalMapList[i].Y;
                    DistBetweenPoints = (float)Math.Abs(Math.Sqrt(Xpel * Xpel + Ypel * Ypel));
                    radius = LedData[g];// para filtrar puntos a más de 4 metros

                    if (DistBetweenPoints < h)
                    {
                        h = DistBetweenPoints;
                    }

                    if (h > 0.01 && radius < 2 && i == GlobalMapList.Count - 1 && radius > 0.2)//era 0.01
                    {
                        GlobalMapList.Add(new ObstaclesPoint { X = LedDataMass[g, 0], Y = LedDataMass[g, 1], weight = 2 });
                    }
                }
            }

            filterGlobalMapList(GlobalMapList, LedData, OdomData, LedDataMass);//enviar una hoja a una función para filtrar puntos                  
        }

        void filterGlobalMapList(List<ObstaclesPoint> GlobalMapList, float[] LedData, float[] OdomData, float[,] LedDataMass)
        {

            for (int i = 0; i < GlobalMapList.Count; i++)
            {
                for (int g = 0; g < LedData.Length; g++)
                {
                    float Tg = (float)Math.Atan(GlobalMapList[i].X / GlobalMapList[i].Y);
                    float Tg2 = (float)Math.Atan(LedDataMass[g, 0] / LedDataMass[g, 1]);
                    float dx = Math.Abs(GlobalMapList[i].X) - Math.Abs(LedDataMass[g, 0]);
                    float dy = Math.Abs(GlobalMapList[i].Y) - Math.Abs(LedDataMass[g, 1]);
                    float da = (float)Math.Abs(Tg - Tg2);
                    float Xpel = GlobalMapList[i].X - OdomData[0];
                    float Ypel = GlobalMapList[i].Y - OdomData[1];
                    float TargetonPoint = (float)Math.Atan2(Xpel, Ypel);
                    float TT = (float)Math.Abs(TargetonPoint - OdomData[2]);
                    float XpelP = LedDataMass[g, 0] - GlobalMapList[i].X;//para calcular la distancia entre puntos
                    float YpelP = LedDataMass[g, 1] - GlobalMapList[i].Y;
                    float DistBetweenPoints = (float)Math.Abs(Math.Sqrt(XpelP * XpelP + YpelP * YpelP));

                    if (LedData[g] < 2 && DistBetweenPoints > 0.1 && da < 0.005 && TT < 1.5) //compruebe si el punto anterior está oculto por el nuevo y luego elimínelo
                    {
                        // GlobalMapList.RemoveAt(i);//Eliminación comentada de puntos inexistentes, temporalmente.
                        //   break;
                    }
                }
            }
        }

        public float[,] graph;
        public int Ymax = 180;
        public int Xmax = 180;

        public void GlobListToGraph(List<ObstaclesPoint> GlobalMapList, float[] OdomData)//método para convertir una hoja en una matriz
        {
            graph = new float[Xmax, Ymax];//matriz que es un gráfico ponderado
            int ymatrix = 0;
            int xmatrix = 0;

            for (int k = 0; k < Xmax; k++)
            {
                for (int k2 = 0; k2 < Ymax; k2++)
                {
                    graph[k, k2] = 1;
                }
            }

            for (int i = 0; i < GlobalMapList.Count; i++)
            {
                float Tx = GlobalMapList[i].X * 10;
                float Ty = GlobalMapList[i].Y * 10;
                xmatrix = (int)Math.Floor(Tx);
                ymatrix = (int)Math.Floor(Ty);
                graph[xmatrix + Xmax / 2, ymatrix + Ymax / 2] = GlobalMapList[i].weight;
            }
        }
    }
}
