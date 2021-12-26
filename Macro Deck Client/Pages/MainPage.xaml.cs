﻿using SuchByte.MacroDeck.Settings;
using SuchByte.MacroDeck.Utils;
using SuchByte.MacroDeck.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace SuchByte.MacroDeck
{
    public partial class MainPage : ContentPage
    {
        private string _clientId;
        public string ClientId { get { return this._clientId; } }

        private ClientSettings _settings = new ClientSettings();
        public ClientSettings Settings
        {
            get
            {
                return this._settings;
            }
        }

        private double _width = 0;
        private double _height = 0;

        DeckPage _deckPage;

        List<string> _recentConnections = new List<string>();

        public string Version
        {
            get
            {
                var version = DependencyService.Get<IAppVersionProvider>();
                return version.AppVersion;
            }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height); //must be called
            if (width != this._width || height != this._height)
            {
                this._width = width;
                this._height = height;
                this.connection.Orientation = (this._width > this._height) ? StackOrientation.Horizontal : StackOrientation.Vertical; // Responsive
                this.foundDevicesList.Children.Clear();
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            BroadcastReceiver.Start();
        }

        public MainPage()
        {
            InitializeComponent();
            this._deckPage = new DeckPage(this);
            BindingContext = this;
            this._deckPage.ConnectionStateChanged += OnConnectionStateChanged;
            this._deckPage.SettingsChanged += OnSettingsChanged;

            try
            {
                if (Preferences.ContainsKey("recent-connections"))
                {
                    var recentConnectionsJson = Preferences.Get("recent-connections", "");
                    this._recentConnections = JsonConvert.DeserializeObject<List<string>>(recentConnectionsJson);
                }
            } catch { }

            this.LoadRecentConnections();

            try
            {
                if (Preferences.ContainsKey("client-id"))
                {
                    this._clientId = Preferences.Get("client-id", "");
                } else
                {
                    this._clientId = RandomStringGenerator.RandomString(9);
                    Preferences.Set("client-id", this._clientId);
                }
                this.lblClientId.Text = "Client id: " + this._clientId;
            }
            catch { }
            BroadcastReceiver.DeviceFound += BroadcastReceiver_DeviceFound;
        }

        private void BroadcastReceiver_DeviceFound(object sender, DeviceFoundEventArgs e)
        {
            if (!this.IsVisible || this.foundDevicesList.Children.Count >= 3) return;
            Device.BeginInvokeOnMainThread(() => {
                foreach (DeviceItem foundDevicesListItem in this.foundDevicesList.Children)
                {
                    if (foundDevicesListItem.DeviceName.Equals(e.ComputerName))
                    {
                        foundDevicesListItem.Parent = this.foundDevicesList;
                        foundDevicesListItem.IsVisible = true;
                        return;
                    }
                }

                DeviceItem deviceItem = new DeviceItem(e.ComputerName, e.Host);
                deviceItem.ItemTapped += DeviceItemTapped;
                this.foundDevicesList.Children.Add(deviceItem);
            });            
        }

        private void DeviceItemTapped(object sender, EventArgs e)
        {
            DeviceItem deviceItem = sender as DeviceItem;
            string hostName = deviceItem.Connection.Split(':')[0];
            string port = deviceItem.Connection.Split(':')[1];
            this.hostName.Text = hostName;
            this.port.Text = port;
            this.Connect();
        }

        private void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            switch (e.State)
            {
                case ConnectionState.CONNECTED:
                    BroadcastReceiver.Stop();
                    if (!this._recentConnections.Contains(this._deckPage.Host + ":" + this._deckPage.Port))
                    {
                        this._recentConnections.Add(this._deckPage.Host + ":" + this._deckPage.Port);
                    }
                    this.SaveRecentConnections();

                    break;
                case ConnectionState.ERROR:
                    DisplayAlert("Error", "Connection refused", "OK");
                    try
                    {
                        Navigation.PopToRootAsync();
                    }
                    catch { }
                    break;
                case ConnectionState.CLOSED:
                    try
                    {
                        Navigation.PopToRootAsync();
                    } catch { }
                    break;
            }
        }

        public void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            this._settings = e.Settings;
            var brightnessService = DependencyService.Get<IBrightnessService>();
            brightnessService.SetBrightness(this._settings.Brightness);
        }

        private void LoadRecentConnections()
        {
            foreach (ConnectionItem connectionItem in this.recentConnectionList.Children)
            {
                connectionItem.ItemTapped -= this.RecentConnectionItemTapped;
                connectionItem.DeleteTapped -= this.RecentConnectionItemDeleteTapped;
            }
            this.recentConnectionList.Children.Clear();
            foreach (string connection in this._recentConnections)
            {
                ConnectionItem connectionItem = new ConnectionItem(connection);
                connectionItem.ItemTapped += this.RecentConnectionItemTapped;
                connectionItem.DeleteTapped += this.RecentConnectionItemDeleteTapped;
                this.recentConnectionList.Children.Add(connectionItem);
            }
            //this.recentConnections.IsVisible = this._recentConnections.Count > 0;
        }

        private void RecentConnectionItemDeleteTapped(object sender, EventArgs e)
        {
            ConnectionItem connectionItem = sender as ConnectionItem;
            if (this._recentConnections.Contains(connectionItem.Connection))
            {
                this._recentConnections.Remove(connectionItem.Connection);
            }
            this.SaveRecentConnections();
        }

        private void RecentConnectionItemTapped(object sender, EventArgs e)
        {
            ConnectionItem connectionItem = sender as ConnectionItem;
            string hostName = connectionItem.Connection.Split(':')[0];
            string port = connectionItem.Connection.Split(':')[1];
            this.hostName.Text = hostName;
            this.port.Text = port;
            this.Connect();
        }

        private void SaveRecentConnections()
        {
            try
            {
                var recentConnectionsJson = JsonConvert.SerializeObject(this._recentConnections);
                Preferences.Set("recent-connections", recentConnectionsJson);
            }
            catch { }
            this.LoadRecentConnections();
        }
        private void Connect_Clicked(object sender, EventArgs e)
        {
            this.Connect();
        }

        private void hostName_Completed(object sender, EventArgs e)
        {
            
        }

        private void Connect()
        {
            if (String.IsNullOrWhiteSpace(this.hostName.Text))
            {
                DisplayAlert("Error", "Please type in the hostname/IP address of your Macro Deck host", "OK");
                return;
            }

            int port = -1;
            Int32.TryParse(this.port.Text, out port);
            if (port <= 0 || port >= 65535)
            {
                DisplayAlert("Error", "The port must be a number between 1 and 65535", "OK");
                return;
            }

            try
            {
                if (this._deckPage == null) return;
                Navigation.PushAsync(this._deckPage);
                this._deckPage.Open(this.hostName.Text.Replace(Environment.NewLine, String.Empty), port);
            } catch { }
        }

        private void hostName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string lastCharacter;
            if (!String.IsNullOrEmpty(e.NewTextValue) && e.NewTextValue.Length > 0)
            {
                lastCharacter = e.NewTextValue.Substring(e.NewTextValue.Length - 1, 1);
                if (lastCharacter == Environment.NewLine)
                {
                    this.hostName.Text = e.OldTextValue.Replace(Environment.NewLine, String.Empty);
                    this.Connect();
                }
            }
            
        }

    }
}
