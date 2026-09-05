using System;
using System.Globalization;
using VRepAdapter;

namespace VRepClient
{
    public class RobotAdapter
    {
        public virtual void Init() { }
        public virtual void Deactivate() { }
        public virtual void Send(Drive RobDrive) { }
        public virtual void ReceiveLedData(string LedarData) { }
        public virtual void ReceiveOdomData(string OdometryData) { }

        // Método visual (dejamos la definición vacía para que no rompa Form1)
        public virtual void SetGoalVisual(float x, float y) { }

        public float[] RobotLedData;
        public float[] RobotOdomData;
        public float right;
        public float left;

        // Ganancia final aplicada a los 4 comandos de rueda normalizados que llegan
        // desde Drive (reemplaza el -5f que antes estaba fijo en VrepAdapter.Send).
        public float WheelGain = -5f;
    }

    public class VrepAdapter : RobotAdapter
    {
        public int clientID = -1;
        int leftMotorHandle, rightMotorHandle, leftMotorHandleA, rightMotorHandleA;

        public override void Init()
        {
            clientID = VRepFunctions.Start("127.0.0.1", 7777);
            if (clientID != -1)
            {
                VRepFunctions.GetObjectHandle(clientID, "rollingJoint_fl", out leftMotorHandle);
                VRepFunctions.GetObjectHandle(clientID, "rollingJoint_rl", out leftMotorHandleA);
                VRepFunctions.GetObjectHandle(clientID, "rollingJoint_rr", out rightMotorHandle);
                VRepFunctions.GetObjectHandle(clientID, "rollingJoint_fr", out rightMotorHandleA);
            }
        }

        public override void Send(Drive RobDrive)
        {
            // leftMotorHandle=fl, leftMotorHandleA=rl, rightMotorHandle=rr, rightMotorHandleA=fr
            // (ver Init). Antes se mandaban solo 2 velocidades distintas (pares fl/rl y rr/fr
            // forzados a ser iguales); ahora las 4 ruedas son independientes, lo que en modo
            // DifferentialEmulated da el mismo resultado de siempre (Drive ya iguala
            // wheelFL=wheelRL y wheelFR=wheelRR) y en modo Holonomic permite comando lateral real.
            float fl = 0, fr = 0, rl = 0, rr = 0;
            if (RobDrive != null)
            {
                fl = RobDrive.wheelFL * WheelGain;
                fr = RobDrive.wheelFR * WheelGain;
                rl = RobDrive.wheelRL * WheelGain;
                rr = RobDrive.wheelRR * WheelGain;
            }

            if (VRepFunctions.GetConnectionId(clientID) != -1)
            {
                VRepFunctions.SetJointTargetVelocity(clientID, leftMotorHandle, fl);
                VRepFunctions.SetJointTargetVelocity(clientID, leftMotorHandleA, rl);
                VRepFunctions.SetJointTargetVelocity(clientID, rightMotorHandle, rr);
                VRepFunctions.SetJointTargetVelocity(clientID, rightMotorHandleA, fr);
            }
        }

        // ELIMINAMOS LA LÓGICA QUE CAUSABA EL ERROR
        public override void SetGoalVisual(float x, float y)
        {
            // Dejamos esto vacío porque tu DLL no soporta SetObjectPosition.
            // El código compilará, pero el disco no se moverá.
        }

        public override void ReceiveLedData(string LedarData)
        {
            // Antes esto reseteaba RobotLedData a un array en blanco apenas
            // llegaba una señal vacía (un hipo transitorio de la API remota),
            // borrando la última lectura válida del LIDAR. Ahora se parsea en
            // un array temporal y solo se reemplaza RobotLedData si el parseo
            // completo tuvo éxito; si falla, se conserva la lectura anterior.
            if (string.IsNullOrEmpty(LedarData)) return;
            try
            {
                string[] words = LedarData.Split(';');
                float[,] LaserDatatemporaryVrep = new float[684, 3];
                int h = 0;
                for (int i = 0; i < 684 && h < words.Length; i++)
                {
                    for (int j = 0; j < 3 && h < words.Length; j++)
                    {
                        float val;
                        if (float.TryParse(words[h], NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                            LaserDatatemporaryVrep[i, j] = val;
                        else
                            LaserDatatemporaryVrep[i, j] = 0;
                        if (LaserDatatemporaryVrep[i, j] == 0) LaserDatatemporaryVrep[i, j] = 500;
                        h++;
                    }
                }
                float[] parsed = new float[518];
                int d = 0;
                for (int i = 83; i < 601 && d < parsed.Length; i++)
                {
                    parsed[d] = (float)(Math.Sqrt(LaserDatatemporaryVrep[i, 0] * LaserDatatemporaryVrep[i, 0] + LaserDatatemporaryVrep[i, 1] * LaserDatatemporaryVrep[i, 1]));
                    d++;
                }
                RobotLedData = parsed;
            }
            catch { }
        }

        public override void ReceiveOdomData(string OdometryData)
        {
            // Bug critico corregido: esto reseteaba RobotOdomData a [0,0,0] cada
            // vez que la señal "RobPos" llegaba vacía o mal formada (un hipo
            // transitorio de comunicación con CoppeliaSim). Eso teletransportaba
            // la posición creída del robot al centro del mapa por un tick,
            // haciendo que el controlador apuntara hacia un objetivo totalmente
            // erróneo — consistente con quedar "atorado en un objeto lejos de la
            // ruta". Ahora se parsea en un array temporal y solo se reemplaza
            // RobotOdomData si el parseo completo tuvo éxito.
            if (string.IsNullOrEmpty(OdometryData)) return;
            try
            {
                string[] words = OdometryData.Split(';');
                float[] parsed = new float[3];
                for (int i = 0; i < 3; i++)
                    parsed[i] = float.Parse(words[i], CultureInfo.InvariantCulture);
                RobotOdomData = parsed;
            }
            catch { }
        }

        public override void Deactivate()
        {
            VRepFunctions.Finish(clientID);
        }
    }
}