using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VRepClient
{
    public partial class MapForm : Form
    {
        public MapForm()
        {
            
            InitializeComponent();            
        }
       
        protected override void OnPaint(PaintEventArgs e)
        {            
       /*     
            base.OnPaint(e);

            if (Form1.f1.RobDrive != null & Form1.f1.ra != null)
            {
              
                for (int i = 0; i < Form1.f1.map.GlobalMapList.Count; i++)
                {
                    int g = 50;//изменить для смена масштаба
                    //Y умножается на -1 чтобы изначально карта смотрела вертикально
                    e.Graphics.DrawLine(new Pen(Color.Black), Form1.f1.map.GlobalMapList[i].X * g + 250, Form1.f1.map.GlobalMapList[i].Y * (-1) * g + 300, Form1.f1.map.GlobalMapList[i].X * g + 251, Form1.f1.map.GlobalMapList[i].Y * (-1) * g + 301);
                   
                    e.Graphics.DrawImage(Form1.f1.Rob, Form1.f1.map.RobOdData[0]*g + 240, Form1.f1.map.RobOdData[1]*g*(-1) + 290);
                    //вызов функции ниже должен быть не здесь а в классе ScreachonGraph                
                    

                    int f = 0;             
                }

           }
        */      
        }

        /*private void DrawLine(Pen pen, Point point1, Point point2)
        {
         
        }*/
        private void MapForm_Load(object sender, EventArgs e)
        {

        }
    }
}
