// ---------------------------------------------------------------------------------
//  
// The MIT License(MIT)
//  
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal 
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions: 
//  
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software. 
//   
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN 
// THE SOFTWARE. 
// ---------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using System.Diagnostics;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Storage.Streams;
using Windows.Networking.Sockets;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using System.Collections.ObjectModel;
using Windows.Devices.Enumeration;
using Windows.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using Windows.UI.Core;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;


using Windows.ApplicationModel.Background;
using Windows.Foundation.Diagnostics;
using System.Diagnostics;
using System.Threading.Tasks;



// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace IOTCoreFirstRun
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private CoreDispatcher dispatcher;



        public ObservableCollection<RfcommChatDeviceDisplay> ResultCollection
        {
            get;
            private set;
        }


        //public TpmDevice tpm = null;

        #region RFCOMM server

        private StreamSocket socket;
        private DataWriter writer;
        private RfcommServiceProvider rfcommProvider;
        private StreamSocketListener socketListener;

        public async Task InitRfcommServer()
        {
            // this one inits the RFCOMM Comminication server to access the device if the there is no WLAN connection existing 
            Debug.WriteLine("Init RFCOMM Server");
            try
            {
                rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(Constants.RfcommChatServiceUuid));
            }
            // Catch exception HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE).
            catch (Exception ex) when ((uint)ex.HResult == 0x800710DF)
            {
                // The Bluetooth radio may be off.
                Debug.WriteLine("Make sure your Bluetooth Radio is on: " + ex.Message);
                return;
            }


            // Create a listener for this service and start listening
            socketListener = new StreamSocketListener();
            socketListener.ConnectionReceived += OnConnectionReceived;
            var rfcomm = rfcommProvider.ServiceId.AsString();

            await socketListener.BindServiceNameAsync(rfcommProvider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            // Set the SDP attributes and start Bluetooth advertising
            InitializeServiceSdpAttributes(rfcommProvider);

            try
            {
                rfcommProvider.StartAdvertising(socketListener, true);
            }
            catch (Exception e)
            {
                // If you aren't able to get a reference to an RfcommServiceProvider, tell the user why.  Usually throws an exception if user changed their privacy settings to prevent Sync w/ Devices.  
                Debug.WriteLine(e.Message);
                return;
            }

            Debug.WriteLine("Listening for incoming connections");
        }

        /// <summary>
        /// Creates the SDP record that will be revealed to the Client device when pairing occurs.  
        /// </summary>
        /// <param name="rfcommProvider">The RfcommServiceProvider that is being used to initialize the server</param>
        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider)
        {
            var sdpWriter = new DataWriter();

            // Write the Service Name Attribute.
            sdpWriter.WriteByte(Constants.SdpServiceNameAttributeType);

            // The length of the UTF-8 encoded Service Name SDP Attribute.
            sdpWriter.WriteByte((byte)Constants.SdpServiceName.Length);

            // The UTF-8 encoded Service Name value.
            sdpWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpWriter.WriteString(Constants.SdpServiceName);

            // Set the SDP Attribute on the RFCOMM Service Provider.
            rfcommProvider.SdpRawAttributes.Add(Constants.SdpServiceNameAttributeId, sdpWriter.DetachBuffer());
        }

        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            // Don't need the listener anymore
            socketListener.Dispose();
            socketListener = null;

            try
            {
                socket = args.Socket;
            }
            catch (Exception e)
            {
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Debug.WriteLine(e.Message);
                });
                Disconnect();
                return;
            }

            // Note - this is the supported way to get a Bluetooth device from a given socket
            var remoteDevice = await BluetoothDevice.FromHostNameAsync(socket.Information.RemoteHostName);

            writer = new DataWriter(socket.OutputStream);
            var reader = new DataReader(socket.InputStream);
            bool remoteDisconnection = false;

            await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Debug.WriteLine("Connected to Client: " + remoteDevice.Name);
                SendMessage("IOTCore Master App:");
            });

            // Infinite read buffer loop
            while (true)
            {
                try
                {
                    // Based on the protocol we've defined, the first uint is the size of the message
                    uint readLength = await reader.LoadAsync(sizeof(uint));

                    // Check if the size of the data is expected (otherwise the remote has already terminated the connection)
                    if (readLength < sizeof(uint))
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    uint currentLength = reader.ReadUInt32();

                    // Load the rest of the message since you already know the length of the data expected.  
                    readLength = await reader.LoadAsync(currentLength);

                    // Check if the size of the data is expected (otherwise the remote has already terminated the connection)
                    if (readLength < currentLength)
                    {
                        remoteDisconnection = true;
                        break;
                    }
                    string message = reader.ReadString(currentLength);

                    await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        Debug.WriteLine("Received: " + message);
                        if (message.ToUpper() == "?")
                        {
                            SendMessage("GETWLAN");
                            SendMessage("GETCURRENTNET");
                            SendMessage("CONNECTWLANPW,[SSID],[PASSWORD]");

                        }
                        else if (message.ToUpper() == "GETWLAN")
                        {
                            NetworkPresenter np = new NetworkPresenter();
                            await Task.Delay(100);
                            var networklist = await np.GetAvailableNetworks();
                            foreach (WiFiAvailableNetwork network in networklist)
                            {
                                SendMessage("WLAN,NETINFO," + network.Ssid + "," + network.SecuritySettings.NetworkAuthenticationType.ToString());
                            }

                        }
                        else if (message.ToUpper() == "GETCURRENTNET")
                        {
                            NetworkPresenter np = new NetworkPresenter();
                            await Task.Delay(100);
                            var networklist = await np.GetAvailableNetworks();
                            WiFiAvailableNetwork network = np.GetCurrentWifiNetwork();
                            if (network != null)
                            {
                                SendMessage("WLAN,CURRENTNET,CONNECTED," + network.Ssid + "," + network.SecuritySettings.NetworkAuthenticationType.ToString() + ',' + NetworkPresenter.GetCurrentIpv4Address());
                            }
                            else
                            {
                                SendMessage("WLAN,CURRENTNET,DISCONNECTED");
                            }
                        }
                        else if (message.ToUpper().Contains("CONNECTWLANPW"))
                        {
                            string[] s = message.Split(',');
                            try
                            {
                                string net = s[1];
                                string password = s[2];
                                WiFiAvailableNetwork connectnetwork = null;
                                NetworkPresenter np = new NetworkPresenter();
                                await Task.Delay(100);
                                var networklist = await np.GetAvailableNetworks();
                                foreach (WiFiAvailableNetwork network in networklist)
                                {
                                    if (network.Ssid == net)
                                    {
                                        connectnetwork = network;
                                        break;
                                    }
                                }
                                if (connectnetwork == null)
                                {
                                    SendMessage("CONNECTWLANPW,ERROR,UNKNOWNSSID");
                                }
                                else
                                {
                                    PasswordCredential credential = new PasswordCredential();
                                    credential.Password = password;
                                    bool result = await np.ConnectToNetworkWithPassword(connectnetwork, true, credential);
                                }
                            }
                            catch
                            {
                                SendMessage("ERROR,COMMAND,SYNTAXERROR");
                            }
                        }
                        else
                        {
                            SendMessage("ERROR,COMMAND,UNKNOWN");
                        }
                    });
                }
                // Catch exception HRESULT_FROM_WIN32(ERROR_OPERATION_ABORTED).
                catch (Exception ex) when ((uint)ex.HResult == 0x800703E3)
                {
                    await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        Debug.WriteLine("Client Disconnected Successfully");
                    });
                    break;
                }
            }

            reader.DetachStream();
            if (remoteDisconnection)
            {
                Disconnect();
                await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    Debug.WriteLine("Client disconnected");
                });
            }
        }


        private async void Disconnect()
        {
            if (rfcommProvider != null)
            {
                rfcommProvider.StopAdvertising();
                rfcommProvider = null;
            }

            if (socketListener != null)
            {
                socketListener.Dispose();
                socketListener = null;
            }

            if (writer != null)
            {
                writer.DetachStream();
                writer = null;
            }

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }
            await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                // restart server; 
                Debug.WriteLine("Restarting Server");
                await InitRfcommServer();
            });
        }

        private async void SendMessage(string text)
        {
            // There's no need to send a zero length message
            if (text.Length != 0)
            {
                // Make sure that the connection is still up and there is a message to send
                if (socket != null)
                {
                    string message = text;
                    writer.WriteUInt32((uint)message.Length);
                    writer.WriteString(message);

                    Debug.WriteLine("Sent: " + message);
                    // Clear the messageTextBox for a new message

                    await writer.StoreAsync();

                }
                else
                {
                    Debug.WriteLine("No clients connected, please wait for a client to connect before attempting to send a message");
                }
            }
        }

        #endregion

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;


            //var device = new TpmDevice(0);
            //try
            //{
            //    string deviceConnectionString = await device.GetConnectionStringAsync();

            //    // Create DeviceClient. Application uses DeviceClient for telemetry messages, device twin
            //    // as well as device management
            //    deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);

            //    // IDeviceTwin abstracts away communication with the back-end.
            //    // AzureIoTHubDeviceTwinProxy is an implementation of Azure IoT Hub
            //    IDeviceTwin deviceTwinProxy = new AzureIoTHubDeviceTwinProxy(deviceClient);

            //    // IDeviceManagementRequestHandler handles device management-specific requests to the app,
            //    // such as whether it is OK to perform a reboot at any givem moment, according the app business logic
            //    // ToasterDeviceManagementRequestHandler is the Toaster app implementation of the interface
            //    IDeviceManagementRequestHandler appRequestHandler = new DeviceManagementRequestHandler();

            //    // Create the DeviceManagementClient, the main entry point into device management
            //    _dmClient = await DeviceManagementClient.CreateAsync(deviceTwinProxy, appRequestHandler);

            //    // Set the callback for desired properties update. The callback will be invoked
            //    // for all desired properties -- including those specific to device management
            //    await deviceClient.SetDesiredPropertyUpdateCallback(OnDesiredPropertyUpdate, null);


            //    string str = "Device Restarted";
            //    var message = new Message(Encoding.ASCII.GetBytes(str));
            //    await deviceClient.SendEventAsync(message);
            //}
            //catch
            //{
            //}


                await InitRfcommServer();

            ConnectCtrl c = new ConnectCtrl(this);
            main.Children.Add(c);
            ResultCollection = new ObservableCollection<RfcommChatDeviceDisplay>();

            c.SetResultList(ResultCollection);
             
            // TODO: 
            // check if you are running in the factory (e.g. contacting a specific server or connecting to a predefined network) 

            // initialize the TPM device 
            //try
            //{
            //    tpm = new TpmDevice(0);
            //}
            //catch
            //{
            //    Debug.WriteLine("TPM not present Error");
            //    return; 
            //}

            //// reset TPM to clean previous 
            //try
            //{
            //    Debug.WriteLine("Reset TPM...");
            //    tpm.Destroy();
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine("TPM was not initialized!"); 
            //}

            //Debug.WriteLine("TPM initialized");
            //string id = tpm.GetDeviceId();

            ////HWID is unique for this device. 
            //string hwid = tpm.GetHardwareDeviceId();
            //Debug.WriteLine("TPM Hardware ID:" + hwid);

            //string hmackey = CryptoKeyGenerator.GenerateKey(32);
            //Debug.WriteLine("TPM hmackey:" + hmackey);

            ////provision the device. 
            //tpm.Provision(hmackey, "gunterlhub.azure-devices.net", hwid);

            // TODO:
            // send hmacky and hwid to production server and create the devices on the iot hub 

            // connect to production server via bluetooth if availiable
            // 

        }
    }

    class Constants
    {
        // The Chat Server's custom service Uuid: 34B1CF4D-1069-4AD6-89B6-E161D79BE4D8
        public static readonly Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");

        // The Id of the Service Name SDP attribute
        public const UInt16 SdpServiceNameAttributeId = 0x100;

        // The SDP Type of the Service Name SDP attribute.
        // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        //    -  the Attribute Type size in the least significant 3 bits,
        //    -  the SDP Attribute Type value in the most significant 5 bits.
        public const byte SdpServiceNameAttributeType = (4 << 3) | 5;

        // The value of the Service Name SDP attribute
        public const string SdpServiceName = "Bluetooth Rfcomm Chat Service";
    }

    public class RfcommChatDeviceDisplay : INotifyPropertyChanged
    {
        private DeviceInformation deviceInfo;

        public RfcommChatDeviceDisplay(DeviceInformation deviceInfoIn)
        {
            deviceInfo = deviceInfoIn;
            UpdateGlyphBitmapImage();
        }

        public DeviceInformation DeviceInformation
        {
            get
            {
                return deviceInfo;
            }

            private set
            {
                deviceInfo = value;
            }
        }

        public string Id
        {
            get
            {
                return deviceInfo.Id;
            }
        }

        public string Name
        {
            get
            {
                return deviceInfo.Name;
            }
        }

        private string _status = null;  
        public string Status
        {
            get { return _status; }
            set { _status = value;  OnPropertyChanged("Status"); }
        }

        private RfcommDeviceService _chatService = null; 
        public RfcommDeviceService chatService
        {
            get { return _chatService; }
            set { _chatService = value; }
        }

        private StreamSocket _chatSocket = null; 
        public StreamSocket chatSocket
        {
            get { return _chatSocket; }
            set { _chatSocket = value; }
        }

        public BitmapImage GlyphBitmapImage
        {
            get;
            private set;
        }

        public void Update(DeviceInformationUpdate deviceInfoUpdate)
        {
            deviceInfo.Update(deviceInfoUpdate);
            UpdateGlyphBitmapImage();
        }

        private async void UpdateGlyphBitmapImage()
        {
            DeviceThumbnail deviceThumbnail = await deviceInfo.GetGlyphThumbnailAsync();
            BitmapImage glyphBitmapImage = new BitmapImage();
            await glyphBitmapImage.SetSourceAsync(deviceThumbnail);
            GlyphBitmapImage = glyphBitmapImage;
            OnPropertyChanged("GlyphBitmapImage");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
 }
