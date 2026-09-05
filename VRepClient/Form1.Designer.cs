namespace VRepClient
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.button1 = new System.Windows.Forms.Button();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.VrepAdapter = new System.Windows.Forms.Button();
            this.Drive = new System.Windows.Forms.Button();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label12 = new System.Windows.Forms.Label();
            this.button4 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(130, 831);
            this.button1.Margin = new System.Windows.Forms.Padding(4);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(115, 36);
            this.button1.TabIndex = 0;
            this.button1.Text = "2. Conectar";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // VrepAdapter
            // 
            this.VrepAdapter.Location = new System.Drawing.Point(9, 831);
            this.VrepAdapter.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.VrepAdapter.Name = "VrepAdapter";
            this.VrepAdapter.Size = new System.Drawing.Size(115, 36);
            this.VrepAdapter.TabIndex = 24;
            this.VrepAdapter.Text = "1. Adaptador";
            this.VrepAdapter.UseVisualStyleBackColor = true;
            this.VrepAdapter.Click += new System.EventHandler(this.VrepAdapter_Click);
            // 
            // Drive
            // 
            this.Drive.Location = new System.Drawing.Point(15, 894);
            this.Drive.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Drive.Name = "Drive";
            this.Drive.Size = new System.Drawing.Size(109, 36);
            this.Drive.TabIndex = 28;
            this.Drive.Text = "3. Iniciar";
            this.Drive.UseVisualStyleBackColor = true;
            this.Drive.UseWaitCursor = true;
            this.Drive.Click += new System.EventHandler(this.Drive_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(44, 169);
            this.pictureBox1.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(850, 850);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox1.TabIndex = 41;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.Click += new System.EventHandler(this.pictureBox1_Click);
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label12.Location = new System.Drawing.Point(12, 956);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(266, 17);
            this.label12.TabIndex = 43;
            this.label12.Text = "JUAN CAMILO ESCOBAR NARANJO";
            this.label12.Click += new System.EventHandler(this.label11_Click);
            // 
            // button4
            // 
            this.button4.Location = new System.Drawing.Point(140, 898);
            this.button4.Margin = new System.Windows.Forms.Padding(4);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(100, 28);
            this.button4.TabIndex = 48;
            this.button4.Text = "ENVIAR";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.ClientSize = new System.Drawing.Size(1239, 922);
            this.Controls.Add(this.Drive);
            this.Controls.Add(this.button4);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.VrepAdapter);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.pictureBox1);
            this.DoubleBuffered = true;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "Form1";
            this.Text = "Youbot Control";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Button VrepAdapter;
        private System.Windows.Forms.Button Drive;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.Button button4;
    }
}

