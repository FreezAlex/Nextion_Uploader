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
        private int MAX_try_page_recieve = 3;
        private int try_page_recieve = 3;
        private bool flag_updated;

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
                txtStatus.Text += "\r\n COM порт: " + selectedPort;
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
                txtStatus.Text += "\r\n Файл: " + firmwareFile;
            }
        }

        // Автоматичний режим
        private void btnAutoMode_Click(object sender, RoutedEventArgs e)
        {
            if(string.IsNullOrEmpty(firmwareFile))
            {
                txtStatus.Text += "\r\n Помилка: Оберіть файл.";
                return;
            }

            if(serialPort.PortName == null || serialPort.PortName == "")
            {
                txtStatus.Text += "\r\n Помилка: Оберіть COM порт.";
                return;
            }

            txtStatus.Text += "\r\n Автоматичний режим...";
            stopAutoMode = false;
            btnStopAutoMode.IsEnabled = true;
            btnManualMode.IsEnabled = false;
            btnSendBauds.IsEnabled = false;
            autoModeThread = new Thread(AutoModeProcess);
            autoModeThread.Start( );
        }

        // Ручний режим
        private void btnManualMode_Click(object sender, RoutedEventArgs e)
        {
            if(!serialPort.IsOpen)
            {
                serialPort.Open( );
            }
            if(string.IsNullOrEmpty(firmwareFile))
            {
                txtStatus.Text += "\r\n Помилка: Оберіть файл.";
                return;
            }

            if(serialPort.PortName == null || serialPort.PortName == "")
            {
                txtStatus.Text += "\r\n Помилка: Оберіть COM порт.";
                return;
            }

            txtStatus.Text += "\r\n Ручний режим...";

            // Підключаємося до дисплея вручну
            if(!TryConnectToDisplay( ))
            {
                txtStatus.Text += "\r\n Помилка: Дісплей не підключено.";
                return;
            }
            // Відправляємо команду для початку прошивки
            SendData($"whmi-wri {new System.IO.FileInfo(firmwareFile).Length},2921600,res0 0x790x790x790xFF0xFF0xFF", baudrate);
            serialPort.Close( );
            serialPort.BaudRate = 2921600;
            serialPort.Open( );
            // Чекаємо відповіді 0x05
            if(!WaitForResponse(0x05))
            {
                txtStatus.Text += "\r\n Помилка: рема відповіді на whmi-wri.";
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
                    txtStatus.Text += "\r\n Помилка: Дані не передано.";
                    return;
                }
            }

            // Чекаємо на фінальну відповідь
            if(!WaitForFinalResponse( ))
            {
                txtStatus.Text += "\r\n Помилка: Кінцеву відповідь не отримано.";
            }
            else
            {
                txtStatus.Text += "\r\n ОК! Завершено успішно.";
            }
        }


        // Відправка повідомлення bauds=1152000xFF0xFF0xFF
        private void btnSendBauds_Click(object sender, RoutedEventArgs e)
        {
            if(serialPort.PortName == null || serialPort.PortName == "")
            {
                txtStatus.Text += "\r\n Помилка: Виберіть COM порт.";
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
            txtStatus.Text += "\r\n Автоматичний режим зупинено.";
            btnStopAutoMode.IsEnabled = false;
            btnManualMode.IsEnabled = true;
            btnSendBauds.IsEnabled = true;
        }

        // Автоматичний процес
        private void AutoModeProcess( )
        {

            while(!stopAutoMode)
            {
            step_1:
                if(stopAutoMode)
                {
                    break;
                }
                if(!TryConnectToDisplay( ))
                {
//                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n Помилка: Дісплей не підключено.");
                    goto step_1;
                }

                SendData($"whmi-wri {new System.IO.FileInfo(firmwareFile).Length},1200000,res0 0x790x790x790xFF0xFF0xFF", baudrate);
                serialPort.Close( );
                serialPort.BaudRate = 1200000;
                serialPort.Open( );
                try_page_recieve = MAX_try_page_recieve;
                flag_updated = false;

                if(!WaitForResponse(0x05))
                {
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n Помилка: нема відповіді на whmi-wri.");
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
                        txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n Помилка: Дані не передано.");
                        goto step_1;
                    }
                }

                if(!WaitForFinalResponse( ))
                {
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n Помилка: кінцеву відповідь не отримано.");
                }
//                else
                {
                    set_bauds_115200( );
                    while(try_page_recieve!=0)
                    {                    
                        Thread.Sleep(1000);
                        SendData("sendme0xFF0xFF0xFF", 115200);
                        if(!WaitForResponse(0x07))
                        {
                            SendData("sendme0xFF0xFF0xFF", 115200);
                            if(!WaitForResponse(0x07))
                            {
                                SendData("sendme0xFF0xFF0xFF", 115200);
                                if(!WaitForResponse(0x07))
                                {
                                    SendData("page 70xFF0xFF0xFF", 115200);
                                    try_page_recieve--;
                                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n Отримую номер сторінки.");
                                }
                            }
                        }
                        else
                        {
                            SendData("t4.txt=\"UPDATED\"0xFF0xFF0xFF", 115200);
                            WaitForResponse(0xff);
                            if(flag_updated == false)
                            {
                                flag_updated = true;
                                txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n ОК! Завершено успішно.");
                            }
                        }
                    }
                    txtStatus.Dispatcher.Invoke(( ) => txtStatus.Text += "\r\n Очікую підключення дісплея");
                }


                //SendData("page 10xFF0xFF0xFF", 115200);
                //Thread.Sleep(2000);
                //SendData("page 20xFF0xFF0xFF", 115200);
                //Thread.Sleep(2000);
                //SendData("page 30xFF0xFF0xFF", 115200);
                //Thread.Sleep(2000);
                //SendData("page 40xFF0xFF0xFF", 115200);
                //Thread.Sleep(2000);
                //SendData("page 50xFF0xFF0xFF", 115200);
                //Thread.Sleep(2000);

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
            DateTime startTime = DateTime.Now;

            while((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                if(serialPort.BytesToRead > 0)
                {
                    byte[] buffer = new byte[serialPort.BytesToRead];
                    serialPort.Read(buffer, 0, buffer.Length);

                    // Перевіряємо, чи з'явились обидва очікувані байти
                    if(buffer.Contains(expectedByte))
                    {
                        return true;
                    }
                }
            }

            return false; // Таймаут
        }
        private bool WaitForResponse(byte expectedByte1, byte expectedByte2)
        {
            int timeout = 500; // 1 секунда
            DateTime startTime = DateTime.Now;

            while((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                if(serialPort.BytesToRead > 0)
                {
                    byte[] buffer = new byte[serialPort.BytesToRead];
                    serialPort.Read(buffer, 0, buffer.Length);

                    // Перевіряємо, чи з'явились обидва очікувані байти
                    if(buffer.Contains(expectedByte1) && buffer.Contains(expectedByte2))
                    {
                        return true;
                    }
                }
            }

            // Якщо таймаут і обидва байти не прийшли
            return false;
        }


        // Очікування фінальної відповіді після передачі
        private bool WaitForFinalResponse( )
        {
            serialPort.BaudRate = baudrate; // Переходимо на швидкість 9600
            byte[] expectedResponse = new byte[] { 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x88, 0xFF, 0xFF, 0xFF };

            // Чекаємо на відповідь протягом 10 секунд
            DateTime startTime = DateTime.Now;
            while((DateTime.Now - startTime).TotalMilliseconds < 10000)
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
            // Якщо відповідь не надійшла або не співпала
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