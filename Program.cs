using System;
using System.Drawing;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;
using HidSharp;

namespace TSG809Battery
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            using (var context = new TrayApplicationContext())
            {
                Application.Run(context);
            }
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private Thread monitorThread;
        private bool running = true;
        
        private int currentBattery = -1;
        private string connectionStatus = "正在连接 TSG809...";

        private const int TargetVendorID = 0xA8A5;
        private const int TargetProductID = 0x2255;

        public TrayApplicationContext()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = CreateTextIcon(88, false),
                ContextMenuStrip = CreateContextMenu(),
                Visible = true,
                Text = "TSG809 鼠标电量监视器"
            };

            monitorThread = new Thread(BatteryMonitorLoop)
            {
                IsBackground = true
            };
            monitorThread.Start();
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            var statusItem = menu.Items.Add("状态: 初始化中...");
            statusItem.Enabled = false;

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = menu.Items.Add("退出程序");
            exitItem.Click += (s, e) => {
                running = false;
                trayIcon.Visible = false;
                Application.Exit();
            };

            return menu;
        }

        private void BatteryMonitorLoop()
        {
            while (running)
            {
                bool foundDevice = false;

                try
                {
                    foreach (var device in DeviceList.Local.GetHidDevices(TargetVendorID, TargetProductID))
                    {
                        if (device.TryOpen(out HidStream hidStream))
                        {
                            foundDevice = true;
                            connectionStatus = "TSG809 已连接";
                            
                            try
                            {
                                // 1. 主动向鼠标发送一个获取电力的请求包（命令字 0x07）
                                int maxOutLen = device.GetMaxOutputReportLength();
                                if (maxOutLen > 0)
                                {
                                    byte[] writeBuf = new byte[maxOutLen];
                                    writeBuf[0] = 0x07; 
                                    hidStream.Write(writeBuf);
                                }

                                // 2. 读取返回的数据
                                hidStream.ReadTimeout = 1500;
                                byte[] buf = new byte[device.GetMaxInputReportLength()];
                                int readBytes = hidStream.Read(buf);
                                
                                if (readBytes > 0)
                                {
                                    string rawHex = "";
                                    for (int i = 0; i < Math.Min(8, buf.Length); i++)
                                    {
                                        rawHex += $"{buf[i]} ";
                                    }
                                    connectionStatus = $"收到数据: [{rawHex}]";
                                    
                                    // 遍历寻找 0 到 100 之间的合理电量值
                                    for (int i = 0; i < buf.Length; i++)
                                    {
                                        if (buf[i] > 0 && buf[i] <= 100)
                                        {
                                            currentBattery = buf[i];
                                            break;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                connectionStatus = "通信中: " + ex.Message;
                            }
                            
                            hidStream.Close();
                            break;
                        }
                    }

                    if (!foundDevice)
                    {
                        connectionStatus = "未检测到 TSG809 接收器";
                        currentBattery = -1;
                    }
                }
                catch (Exception ex)
                {
                    connectionStatus = "异常: " + ex.Message;
                }

                UpdateTrayUI();
                Thread.Sleep(3000);
            }
        }

        private void UpdateTrayUI()
        {
            try
            {
                if (trayIcon.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Items.Count > 0)
                {
                    trayIcon.ContextMenuStrip.Invoke((MethodInvoker)(() => {
                        trayIcon.ContextMenuStrip.Items[0].Text = $"{connectionStatus}";
                    }));
                }

                int displayVal = currentBattery >= 0 ? currentBattery : 0;
                trayIcon.Icon = CreateTextIcon(displayVal, currentBattery >= 0);
                trayIcon.Text = $"TSG809 电量: {(currentBattery >= 0 ? currentBattery + "%" : "未连接")} | {connectionStatus}";
            }
            catch { }
        }

        // 极致清晰：10号字、常规体、纯黑数字，完美契合浅色任务栏
        private Icon CreateTextIcon(int percentage, bool connected)
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

                if (!connected)
                {
                    using (Font f = new Font("Arial", 8, FontStyle.Bold))
                    {
                        g.DrawString("X", f, Brushes.Red, 2, 1);
                    }
                }
                else
                {
                    string text = percentage >= 100 ? "99" : percentage.ToString();
                    
                    using (Font font = new Font("Arial", 10f, FontStyle.Regular))
                    {
                        g.DrawString(text, font, Brushes.Black, 0, 0);
                    }
                }
            }
            return Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
