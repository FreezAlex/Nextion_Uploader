using Microsoft.Win32;
using System.IO.Ports;
using System.Windows;

namespace NEXTION_loader
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow: Window
    {
        private SerialPort serialPort;
        private string firmwareFile;
        private bool stopAutoMode = false;
        private Thread autoModeThread;
        private int baudrate = 9600;
        public MainWindow( )
        {
            InitializeComponent( );
            serialPort = new SerialPort( );
            PopulateComPorts( );
            btnStopAutoMode.IsEnabled = false; // Задаємо кнопку зупинки як неактивну
        }

        // Завантаження доступних COM-портів в ComboBox
        private void PopulateComPorts( )
        {
            cbComPorts.ItemsSource = SerialPort.GetPortNames( );
            if(cbComPorts.Items.Count > 0)
            {
                cbComPorts.SelectedIndex = 0; // За замовчуванням вибираємо перший доступний порт
            }
        }

        // Обробка зміни вибору COM-порту
        private void cbComPorts_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if(cbComPorts.SelectedItem != null)
            {
                string selectedPort = cbComPorts.SelectedItem.ToString();
                serialPort.PortName = selectedPort;
                txtStatus.Text += "\r\nSelected COM Port: " + selectedPort;
            }
        }

        // Вибір файлу прошивки
        private void btnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Firmware Files (*.tft)|*.tft";
            if(openFileDialog.ShowDialog( ) == true)
            {
                firmwareFile = openFileDialog.FileName;
                txtStatus.Text += "\r\nFirmware selected: " + firmwareFile;
            }
        }

        // Автоматичний режим
        private void btnAutoMode_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(firmwareFile))
            {
                txtStatus.Text += "\r\nError: Please select a firmware file first.";
                return;
            }

            if(serialPort.PortName == null || serialPort.PortName == "")
            {
                txtStatus.Text += "\r\nError: Please select a COM port first.";
                return;
            }

            txtStatus.Text += "\r\nStarting Auto Mode...";
            stopAutoMode = false;
            btnStopAutoMode.IsEnabled = true;
            autoModeThread = new Thread(AutoModeProcess);
            autoModeThread.Start( );
        }

        // Ручний режим
        private void btnManualMode_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(firmwareFile))
            {
                txtStatus.Text += "\r\nError: Please select a firmware file first.";
                return;
            }

            if(serialPort.PortName == null || serialPort.PortName == "")
            {
                txtStatus.Text += "\r\nError: Please select a COM port first.";
                return;
            }

            txtStatus.Text += "\r\nStarting Manual Mode...";

            // Підключаємося до дисплея вручну
            if(!TryConnectToDisplay( ))
            {
                txtStatus.Text += "\r\nError: Display connection failed.";
                return;
            }
            // Відправляємо команду для початку прошивки
            SendData($"whmi-wri {new System.IO.FileInfo(firmwareFile).Length},921600,res0 0x790x790x790xFF0xFF0xFF", baudrate);
            serialPort.Close( );
            serialPort.BaudRate = 921600;
            serialPort.Open( );
            // Чекаємо відповіді 0x05
            if(!WaitForResponse(0x05))
            {
                txtStatus.Text += "\r\nError: No response after whmi-wri.";
                return;
            }

            // Передаємо прошивку по 4096 байт
            byte[] firmwareData = System.IO.File.ReadAllBytes(firmwareFile);
            int totalBytes = firmwareData.Length;
            int packetSize = 4096;

            for(int i = 0; i < totalBytes; i += packetSize)
            {
                int remaining = Math.Min(packetSize, totalBytes - i);
                byte[] packet = new byte[remaining];
                Array.Copy(firmwareData, i, packet, 0, remaining);

                SendData(packet);

                if(!WaitForResponse(0x05))
                {
                    txtStatus.Text += "\r\nError: No response after sending data.";
                    return;
                }
            }

            // Чекаємо на фінальну відповідь
            if(!WaitForFinalResponse( ))
            {
                txtStatus.Text += "\r\nError: Final response timeout.";
            }
            else
            {
                txtStatus.Text += "\r\nOK, Firmware update complete.";
            }
        }


        // Відправка повідомлення bauds=1152000xFF0xFF0xFF
        private void btnSendBauds_Click(object sender, RoutedEventArgs e)
        {
            if(serialPort.PortName == null || serialPort.PortName == "")
            {
                txtStatus.Text += "\r\nError: Please select a COM port first.";
                return;
            }
            set_bauds_115200( );

        }
        private void set_bauds_115200( )
        {
            SendData("bauds=1152000xFF0xFF0xFF", 9600);
            Thread.Sleep(200);
            SendData("page 10xFF0xFF0xFF", 115200);
            SendData("rest0xFF0xFF0xFF", 115200);
        }

        // Зупинка автоматичного режиму
        private void btnStopAutoMode_Click(object sender, RoutedEventArgs e)
        {
            stopAutoMode = true;
            txtStatus.Text += "\r\nAuto Mode Stopped.";
            btnStopAutoMode.IsEnabled = false;
        }

        // Автоматичний процес
        private void AutoModeProcess( )
        {

            while(!stopAutoMode)
            {
                step_1:
                if(!TryConnectToDisplay( ))
                {
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\nError: No display detected.");
                    goto step_1;
                }

                SendData($"whmi-wri {new System.IO.FileInfo(firmwareFile).Length},921600,res0 0x790x790x790xFF0xFF0xFF", baudrate);
                serialPort.Close( );
                serialPort.BaudRate = 921600;
                serialPort.Open( );

                if(!WaitForResponse(0x05))
                {
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\nError: No response after whmi-wri.");
                    goto step_1;
                }

                byte[] firmwareData = System.IO.File.ReadAllBytes(firmwareFile);
                int totalBytes = firmwareData.Length;
                int packetSize = 4096;

                for(int i = 0; i < totalBytes; i += packetSize)
                {
                    if(stopAutoMode)
                        return;

                    int remaining = Math.Min(packetSize, totalBytes - i);
                    byte[] packet = new byte[remaining];
                    Array.Copy(firmwareData, i, packet, 0, remaining);

                    SendData(packet);

                    if(!WaitForResponse(0x05))
                    {
                        txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\nError: No response after sending data.");
                        goto step_1;
                    }
                }

                if(!WaitForFinalResponse( ))
                {
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\nError: Final response timeout.");
                }
                else
                {
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\nOK, Firmware update complete.");
                }

                set_bauds_115200( );

                SendData("page 10xFF0xFF0xFF", 115200);
                Thread.Sleep(2000);
                SendData("page 20xFF0xFF0xFF", 115200);
                Thread.Sleep(2000);
                SendData("page 30xFF0xFF0xFF", 115200);
                Thread.Sleep(2000);
                SendData("page 40xFF0xFF0xFF", 115200);
                Thread.Sleep(2000);
                SendData("page 50xFF0xFF0xFF", 115200);
                Thread.Sleep(2000);

            }
        }

        // Підключення до дисплея
        private bool TryConnectToDisplay( )
        {
            baudrate = 9600;
            // Спроба підключення на швидкості 9600
            serialPort.Close( );
            serialPort.BaudRate = baudrate;
            serialPort.Open( );

            // Відправляємо команду для перевірки зв'язку
            SendData("DRAKJHSUYDGBNCJHGJKSHBDN0xFF0xFF0xFF", baudrate);
            SendData("connect0xFF0xFF0xFF", baudrate);
            SendData("0xFF0xFFconnect0xFF0xFF0xFF", baudrate);

            // Чекаємо на відповідь "comok "
            if(WaitForResponse("comok"))
            {
                // Якщо зв'язок встановлено, повертаємо true
                return true;
            }

            baudrate = 115200;
            // Якщо на швидкості 9600 не вдалося підключитися, пробуємо на 115200
            serialPort.Close( );
            serialPort.BaudRate = baudrate;
            serialPort.Open( );

            // Відправляємо команду знову для перевірки
            SendData("DRAKJHSUYDGBNCJHGJKSHBDN0xFF0xFF0xFF", baudrate);
            SendData("connect0xFF0xFF0xFF", baudrate);
            SendData("0xFF0xFFconnect0xFF0xFF0xFF", baudrate);

            // Чекаємо на відповідь "comok "
            if(WaitForResponse("comok"))
            {
                // Якщо зв'язок встановлено, повертаємо true
                return true;
            }
            baudrate = 9600;
            // Якщо на обох швидкостях не вдалося підключитися, повертаємо false
            return false;
        }


        // Відправка даних через UART
        private void SendData(string message,int bauds = 9600)
        {
            if(serialPort.BaudRate != bauds|| !serialPort.IsOpen)
            {
                serialPort.Close( );
                serialPort.BaudRate = bauds;
                serialPort.Open( );
            }
            List<byte> dataToSend = new List<byte>();

            int index = 0;

            while(index < message.Length)
            {
                // Якщо знайшли "0x" (початок шістнадцяткового значення)
                if(message.Substring(index).StartsWith("0x"))
                {
                    // Витягуємо наступні два символи як шістнадцяткове значення
                    string hexValue = message.Substring(index + 2, 2);
                    dataToSend.Add(Convert.ToByte(hexValue, 16)); // Додаємо як байт
                    index += 4; // Пропускаємо "0x" і два символи після нього
                }
                else
                {
                    // Якщо це не шістнадцяткове значення, додаємо символ як байт
                    dataToSend.Add((byte) message[index]);
                    index++;
                }
            }

            // Відправляємо сформовані байти
            serialPort.Write(dataToSend.ToArray( ), 0, dataToSend.Count);
            Thread.Sleep(100);

        }


        // Відправка пакету даних
        private void SendData(byte[] data)
        {
            if(!serialPort.IsOpen)
            {
                serialPort.Open( );
            }

            serialPort.Write(data, 0, data.Length);
        }

        // Очікування відповіді
        private bool WaitForResponse(string expectedResponse)
        {
            string response;
            // Встановлюємо таймер для максимального часу очікування
            int timeout = 1000; // 2 секунди
            DateTime startTime = DateTime.Now;

            // Читаємо з порту, поки не отримаємо відповідь або не перевищимо таймаут
            while((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                if(serialPort.BytesToRead > 0)
                {
                    // Читаємо відповідь
                    response = serialPort.ReadExisting();

                    // Перевіряємо, чи містить відповідь "comok"
                    if(response.Contains(expectedResponse))
                    {
                        // Якщо знайшли "comok", це означає, що зв'язок встановлено
                        return true;
                    }
                }
            }

            // Якщо по таймауту не було знайдено "comok", повертаємо false
            return false;
        }
        private bool WaitForResponse(byte expectedByte)
        {
            int timeout = 1000; // 1 секунда
            int receivedByte;
            DateTime startTime = DateTime.Now;

            while((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                if(serialPort.BytesToRead > 0)
                {
                    receivedByte = serialPort.ReadByte(); // Читаємо по 1 байту

                    if(receivedByte == expectedByte)
                    {
                        return true; // Отримали потрібний байт
                    }
                }
            }

            return false; // Таймаут
        }

        // Очікування фінальної відповіді після передачі
        private bool WaitForFinalResponse( )
        {
            serialPort.BaudRate = baudrate; // Переходимо на швидкість 9600
            byte[] expectedResponse = new byte[] { 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x88, 0xFF, 0xFF, 0xFF };

            // Чекаємо на відповідь протягом 2 секунд
            DateTime startTime = DateTime.Now;
            while((DateTime.Now - startTime).TotalMilliseconds < 15000)
            {
                if(serialPort.BytesToRead > 0)
                {
                    byte[] buffer = new byte[serialPort.BytesToRead];
                    serialPort.Read(buffer, 0, buffer.Length);

                    // Перевіряємо, чи отримана відповідь містить потрібні байти
                    if(buffer.Length >= expectedResponse.Length)
                    {
                        bool isMatch = true;
                        for(int i = 0; i < expectedResponse.Length; i++)
                        {
                            if(buffer[i] != expectedResponse[i])
                            {
                                isMatch = false;
                                break;
                            }
                        }

                        if(isMatch)
                        {
                            // Якщо відповідь співпала
                            return true;
                        }
                    }
                }
            }
            // Якщо за 2 секунди відповідь не надійшла або не співпала
            return false;
        }

        // Закриття порту при закритті програми
        protected override void OnClosed(EventArgs e)
        {
            if(serialPort.IsOpen)
            {
                serialPort.Close( );
            }
            base.OnClosed(e);
        }
    }
}