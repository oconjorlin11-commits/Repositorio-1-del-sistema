namespace Nuevo_Proyecto.Views.FormularioUI
{
    partial class Salir
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
            BtnCerrarSession = new Button();
            BtnSalir = new Button();
            BtnCancelar = new Button();
            label1 = new Label();
            label3 = new Label();
            label4 = new Label();
            label5 = new Label();
            SuspendLayout();
            // 
            // BtnCerrarSession
            // 
            BtnCerrarSession.BackColor = Color.Red;
            BtnCerrarSession.ForeColor = SystemColors.InactiveBorder;
            BtnCerrarSession.Location = new Point(12, 361);
            BtnCerrarSession.Name = "BtnCerrarSession";
            BtnCerrarSession.Size = new Size(205, 70);
            BtnCerrarSession.TabIndex = 0;
            BtnCerrarSession.Text = "Cerrar sessón";
            BtnCerrarSession.UseVisualStyleBackColor = false;
            // 
            // BtnSalir
            // 
            BtnSalir.BackColor = Color.Red;
            BtnSalir.ForeColor = SystemColors.InactiveBorder;
            BtnSalir.Location = new Point(314, 361);
            BtnSalir.Name = "BtnSalir";
            BtnSalir.Size = new Size(186, 70);
            BtnSalir.TabIndex = 1;
            BtnSalir.Text = "Salir";
            BtnSalir.UseVisualStyleBackColor = false;
            // 
            // BtnCancelar
            // 
            BtnCancelar.BackColor = Color.Red;
            BtnCancelar.ForeColor = SystemColors.InactiveBorder;
            BtnCancelar.Location = new Point(629, 361);
            BtnCancelar.Name = "BtnCancelar";
            BtnCancelar.Size = new Size(199, 70);
            BtnCancelar.TabIndex = 2;
            BtnCancelar.Text = "Cancelar";
            BtnCancelar.UseVisualStyleBackColor = false;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 13.875F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label1.Location = new Point(237, 56);
            label1.Name = "label1";
            label1.Size = new Size(367, 50);
            label1.TabIndex = 3;
            label1.Text = "¿Que desea realizar ?";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 12F);
            label3.Location = new Point(12, 203);
            label3.Name = "label3";
            label3.Size = new Size(219, 45);
            label3.TabIndex = 5;
            label3.Text = "Cerrar sessión";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 12F);
            label4.Location = new Point(289, 203);
            label4.Name = "label4";
            label4.Size = new Size(250, 45);
            label4.TabIndex = 6;
            label4.Text = "Salir del sistema";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Font = new Font("Segoe UI", 12F);
            label5.Location = new Point(565, 203);
            label5.Name = "label5";
            label5.Size = new Size(325, 45);
            label5.TabIndex = 7;
            label5.Text = "Cancelar la operación";
            // 
            // Salir
            // 
            AutoScaleDimensions = new SizeF(13F, 32F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(232, 221, 206);
            ClientSize = new Size(896, 552);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label1);
            Controls.Add(BtnCancelar);
            Controls.Add(BtnSalir);
            Controls.Add(BtnCerrarSession);
            Name = "Salir";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Salir";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button BtnCerrarSession;
        private Button BtnSalir;
        private Button BtnCancelar;
        private Label label1;
        private Label label3;
        private Label label4;
        private Label label5;
    }
}