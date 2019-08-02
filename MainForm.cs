using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;

namespace CommTest
{
    public partial class MainForm : Form
    {
        private readonly byte[] _receiveBuffer = new byte[2048];
        private int _receiveCount;
        private int _displayCount;

        public MainForm()
        {           
            InitializeComponent();

            // 枚举串口
            comboBoxPort.Items.AddRange(SerialPort.GetPortNames());
            if (comboBoxPort.Items.Count > 0)
                comboBoxPort.SelectedIndex = 0;

            comboBoxBaudrate.SelectedIndex = 1;

            // 设置默认存盘路径为桌面
            textBoxFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "\\";
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            var dlg = new FolderBrowserDialog();
            dlg.Description = "请选择存盘文件夹：";

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            var folder = dlg.SelectedPath.Trim();
            if (string.IsNullOrEmpty(folder))
                return;

            if (!folder.EndsWith("\\"))
                folder += "\\";
            textBoxFolder.Text = folder;
        }

        private void buttonSend_Click(object sender, EventArgs e)
        {
            try
            {            
                if (string.IsNullOrEmpty(comboBoxPort.Text))
                    throw new Exception("没有可用的串口！");
                
                // 读取要发送的数据
                var sendData = ReadSendData();

                // 验证输出文件是否能成功创建
                if (string.IsNullOrEmpty(textBoxFileName.Text.Trim()))
                    throw new Exception("输出文件名不能为空！");
                var outputFileName = textBoxFolder.Text + textBoxFileName.Text.Trim() + ".txt";
                using (var stream = new FileStream(outputFileName, FileMode.Append))
                {
                }

                // 校验超时值的范围
                int timeout = 0;
                int.TryParse(textBoxTimeout.Text, out timeout);
                if ((timeout < 50) || (timeout > 1000))
                    throw new Exception("超时时间必须在 50ms ~ 1000ms 之间！");

                // 设置读取超时值为 200ms，并打开串口
                var serialPort = new SerialPort(comboBoxPort.Text, int.Parse(comboBoxBaudrate.Text));
                serialPort.ReadTimeout = timeout;
                serialPort.Open();

                // 发送数据
                serialPort.Write(sendData, 0, sendData.Length);

                // 记录发送日志
                using (var stream = new FileStream(outputFileName, FileMode.Append))
                {
                    using (var writer = new StreamWriter(stream, Encoding.Default))
                    {
                        writer.Write(getSendLog(sendData));
                    }
                }

                // 更新显示
                labelSendCount.Text = sendData.Length.ToString();
                var sb = new StringBuilder("\r\nSend :");
                foreach (var b in sendData)
                    sb.AppendFormat(string.Format(" {0:X2}", b));              
                sb.Append("\r\n\r\nReceive :");
                textBoxLog.Text = sb.ToString();
                
                //buttonSend.Enabled = false;

                // 读取数据
                var bw = new BackgroundWorker();
                bw.WorkerReportsProgress = true;
                bw.ProgressChanged += Bw_ProgressChanged;
                bw.RunWorkerCompleted += Bw_RunWorkerCompleted;
                bw.DoWork += Bw_DoWork;
                bw.RunWorkerAsync(serialPort);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Bw_DoWork(object sender, DoWorkEventArgs e)
        {
            _receiveCount = 0;
            _displayCount = 0;

            var bw = sender as BackgroundWorker;
            var serialPort = e.Argument as SerialPort;
            while (true)
            {
                try
                {
                    // 每次尝试读取 16 个字节
                    var readCount = serialPort.Read(_receiveBuffer, _receiveCount, Math.Min(9, _receiveBuffer.Length - _receiveCount));
                    _receiveCount += readCount;

                    // 本次读取结束
                    if (_receiveCount >= 9)
                        break;
                }
                catch (TimeoutException)
                {
                    // 发生超时，本次读取结束
                    break;
                }
            }

            bw.ReportProgress(_receiveCount);
            serialPort.Close();
        }
   
        private void Bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {       
            var sb = new StringBuilder();
            int end = e.ProgressPercentage;
            for (int i = _displayCount; i < end; i++)
                sb.AppendFormat(" {0:X2}", _receiveBuffer[i]);
            _displayCount = end;

            //var sb = new StringBuilder(" 01 04 04 00 20 7C 4E 5A BA"); 

            // 显示接收到的数据
            labelReceiveCount.Text = end.ToString();
            textBoxLog.Text += sb.ToString();         
            textBoxLog.SelectionStart = textBoxLog.Text.Length;
            textBoxLog.ScrollToCaret();
           
        }

        private void Bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (_receiveCount == 9)
                {                 
                    float value = _receiveBuffer[3] * 256 + _receiveBuffer[4];
                    value += (_receiveBuffer[5] * 256 + _receiveBuffer[6]) / 65535f;
                    label10.Text= string.Format("{0:F3}\r\n", value);                   
                }

                //buttonSend.Enabled = true;

                // 记录接收日志
                var outputFileName = textBoxFolder.Text + textBoxFileName.Text.Trim() + ".txt";
                using (var stream = new FileStream(outputFileName, FileMode.Append))
                {
                    using (var writer = new StreamWriter(stream, Encoding.Default))
                        writer.Write(GetReceiveLog(_receiveBuffer, _receiveCount));
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
           
        }

        /// <summary>
        /// 读取要发送的数据
        /// </summary>
        /// <returns>由发送的字符串转换后的 byte[]</returns>
        private byte[] ReadSendData()
        {
            string[] stringArray = textBoxSendData.Text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (stringArray.Length == 0)
                throw new Exception("没有可用的发送数据！");

            var dataArray = new byte[stringArray.Length];
            for (int i = 0; i < dataArray.Length; i++)
            {
                int value = Convert.ToInt32(stringArray[i], 16);
                if ((value < 0) || (value > 255))
                    throw new Exception("发送数据无法转换为有效的十六进制数据！");

                dataArray[i] = (byte)value;
            }

            return dataArray;
        }

        /// <summary>
        /// 生成发送日志
        /// </summary>
        /// <param name="sendData"></param>
        /// <returns></returns>
        private static string getSendLog(byte[] sendData)
        {
            // 记录时间、发送字节数
            var sb = new StringBuilder();
            sb.AppendFormat("({0})_Send({1}):", DateTime.Now.ToString("HH:mm:ss:fff"), sendData.Length);

            // 记录发送数据
            foreach (var b in sendData)
                sb.AppendFormat(string.Format(" {0:X2}", b));

            // 换行
            sb.Append("\r\n");

            return sb.ToString();
        }

        /// <summary>
        /// 生成接收日志
        /// </summary>
        /// <param name="receiveData"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private static string GetReceiveLog(byte[] receiveData, int count)
        {
            var sb = new StringBuilder();

            // 记录时间、接收字节数
            sb.AppendFormat("({0})_Receive({1}):", DateTime.Now.ToString("HH:mm:ss:fff"), count);

            // 记录接收数据
            for (int i = 0; i < count; i++)
                sb.AppendFormat(" {0:X2}", receiveData[i]);

            // 换行
            sb.Append("\r\n\r\n");

            return sb.ToString();
        }

        private void buttontimer_Click(object sender, EventArgs e)
        {                             
            timer1.Enabled = true;
            timer1.Interval = 500;          
        }
       
    }
}
