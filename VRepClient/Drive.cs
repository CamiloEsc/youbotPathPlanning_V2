using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VRepClient
{
    public class Drive
    {
        public float right, left, Phi;
        public float TargetDirection;
        public float RobotDirection;//variable para salida al formulario a través del formulario
        public float DistToTarget;

        // Comandos de rueda independientes (mecanum). En modo diferencial-emulado
        // (GetDrive) quedan wheelFL=wheelRL=left y wheelFR=wheelRR=right, reproduciendo
        // exactamente el comportamiento original. En modo holonómico (GetHolonomicDrive)
        // las 4 ruedas reciben comandos genuinamente independientes.
        public float wheelFL, wheelFR, wheelRL, wheelRR;

        // Parámetros antes hardcodeados (0.03 y 0.4), ahora inyectados desde ExperimentConfig
        // para poder correr un barrido de tolerancia sin recompilar.
        public float ArrivalTolerance = 0.03f;
        public float RotationThreshold = 0.4f;

        // Multiplicadores de signo (+1/-1) para calibrar el modo holonómico
        // sin recompilar (ver ExperimentConfig.HolonomicVxSign/VySign/OmegaSign
        // y experiment.cfg). La primera prueba real mostró al robot yendo de
        // costado hacia atrás en vez de hacia el objetivo — el signo correcto
        // de mezcla mecanum depende de la orientación real de los rodillos en
        // el modelo de CoppeliaSim, que no se puede verificar sin probar.
        public float VxSign = 1f;
        public float VySign = 1f;
        public float OmegaSign = 1f;

        // Geometría estándar publicada del KUKA youBot, usada solo para fijar la
        // proporción de mezcla traslación/rotación en la cinemática mecanum inversa
        // de abajo. vx, vy y omega se manejan normalizados (no en m/s ni rad/s reales),
        // así que el radio de rueda no participa de la mezcla; la escala física final
        // se calibra empíricamente con WheelGain (RobotAdapter), igual que ya se hacía
        // con el factor -5f del modo diferencial. Verificar el signo de las 4 ruedas
        // contra el modelo real en CoppeliaSim antes de confiar en una corrida larga.
        private const float YoubotHalfLength = 0.235f; // m, semi-distancia entre ejes delantero/trasero
        private const float YoubotHalfWidth = 0.15f;   // m, semi-distancia entre ruedas izquierda/derecha

        // Fija en left/right/wheelFL..RR los 4 campos de rueda para una velocidad
        // diferencial dada (frenado, giro de búsqueda, etc.), manteniendo consistentes
        // los dos modos de control.
        public void SetDifferential(float leftSpeed, float rightSpeed)
        {
            left = leftSpeed;
            right = rightSpeed;
            wheelFL = leftSpeed;
            wheelRL = leftSpeed;
            wheelFR = rightSpeed;
            wheelRR = rightSpeed;
        }

        // Geometría compartida entre los dos modos de control: calcula Phi (error de
        // rumbo) y DistToTarget a partir de la pose del robot y el punto perseguido.
        private void ComputeGeometry(float RobX, float RobY, float RobA, float GoalPointX, float GoalPointY, float Xmax, float Ymax)
        {
            GoalPointX = GoalPointX * 0.1f;
            GoalPointY = GoalPointY * 0.1f;
            Xmax = Xmax * 0.1f;
            Ymax = Ymax * 0.1f;
            RobX = RobX + Xmax / 2;
            RobY = RobY + Ymax / 2;
            RobotDirection = RobA;
            //determinar la dirección relativa del objetivo
            float Xpel = GoalPointX - RobX;
            float Ypel = GoalPointY - RobY;
            TargetDirection = (float)Math.Atan2(Xpel, Ypel);  //solo necesitas a RobA-
            DistToTarget = (float)Math.Sqrt(Xpel * Xpel + Ypel * Ypel);

            if (TargetDirection - RobA < Math.PI && TargetDirection - RobA > -Math.PI)
            {
                Phi = TargetDirection - RobA;
            }
            else
            {
                if ((Math.PI * 2) > Math.Abs((float)(Math.PI * 2 + TargetDirection - RobA)))//si el ángulo entre los puntos es mayor que dos pi
                {
                    Phi = (float)(Math.PI * 2 + TargetDirection - RobA);
                }
                else
                {
                    Phi = (TargetDirection - RobA - (float)(Math.PI * 2));
                }
            }
        }

        // Control diferencial-emulado (comportamiento original, ahora parametrizado):
        // fuerza giro en el sitio cuando |Phi| > RotationThreshold, si no avanza con
        // corrección proporcional. Frena contra ArrivalTolerance.
        public void GetDrive(float RobX, float RobY, float RobA, float GoalPointX, float GoalPointY, float Xmax, float Ymax)
        {
            ComputeGeometry(RobX, RobY, RobA, GoalPointX, GoalPointY, Xmax, Ymax);

            //Determinamos si el robot está muy desviado del objetivo y lo dirigimos hacia él.
            if (Phi > RotationThreshold || Phi < -RotationThreshold)
            {
                if (Phi > RotationThreshold) { right = -1; left = 1; }
                if (Phi < -RotationThreshold) { right = 1; left = -1; }
            }

            if (Phi < RotationThreshold || Phi > -RotationThreshold)
            {
                if (Phi < RotationThreshold && Phi > 0) { right = 1 - Phi * 1.4f; left = 1; }
                if (Phi > -RotationThreshold && Phi < 0) { right = 1; left = 1 - Phi * 1.4f * -1; }
            }

            if (Phi == 0)
            {
                right = 1; left = 1;
            }

            if (DistToTarget < ArrivalTolerance)
            {
                right = 0;
                left = 0;
            }

            wheelFL = left; wheelRL = left;
            wheelFR = right; wheelRR = right;
        }

        // Contraparte holonómica real: en vez de forzar parada-giro-arranque cuando el
        // error de rumbo es grande, avanza directo hacia el objetivo combinando
        // traslación frontal y lateral (vx, vy) más una corrección de rumbo suave en
        // paralelo (omega), y reparte el comando a las 4 ruedas mecanum de forma
        // independiente. Usa el mismo criterio de llegada (ArrivalTolerance) que el
        // modo diferencial para que la comparación A/B sea justa.
        public void GetHolonomicDrive(float RobX, float RobY, float RobA, float GoalPointX, float GoalPointY, float Xmax, float Ymax)
        {
            ComputeGeometry(RobX, RobY, RobA, GoalPointX, GoalPointY, Xmax, Ymax);

            if (DistToTarget < ArrivalTolerance)
            {
                wheelFL = 0; wheelFR = 0; wheelRL = 0; wheelRR = 0;
                left = 0; right = 0;
                return;
            }

            float vx = VxSign * (float)Math.Cos(Phi); // componente frontal (marco del robot)
            float vy = VySign * (float)Math.Sin(Phi); // componente lateral (marco del robot)
            float omega = OmegaSign * Clamp(Phi * 0.5f, -1f, 1f); // corrección de rumbo suave, no bloqueante

            float L = YoubotHalfLength + YoubotHalfWidth;
            wheelFL = vx - vy - L * omega;
            wheelFR = vx + vy + L * omega;
            wheelRL = vx + vy - L * omega;
            wheelRR = vx - vy + L * omega;

            // left/right quedan como promedio aproximado, solo para paneles que aún
            // lean estos dos campos; el control real usa las 4 ruedas independientes.
            left = (wheelFL + wheelRL) / 2f;
            right = (wheelFR + wheelRR) / 2f;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
