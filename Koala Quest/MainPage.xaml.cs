using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
// The following are required for the Windows Phone Plugin
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Media;
using System.IO;
using System.Runtime.Serialization.Json;
using Microsoft.Devices;
using System.Threading.Tasks;
using Microsoft.Phone.Marketplace;
using Microsoft.Phone.Tasks;
using System.Globalization;
#if DEBUG
using MockIAPLib;
using Store = MockIAPLib;
#else
using Windows.ApplicationModel.Store;
using Store = Windows.ApplicationModel.Store;
#endif

namespace KoalaQuest
{
    public partial class MainPage : PhoneApplicationPage
    {
        // Constructor
        public MainPage()
        {
            InitializeComponent();
        }

        ListingInformation li;

        private void Browser_Loaded(object sender, RoutedEventArgs e)
        {
            // Load IAP Listing Information

#if DEBUG

            //Use Mock API instead of real store
            MockIAP.Init();
            MockIAP.RunInMockMode(true);
            MockIAP.SetListingInformation(1, "en-us", "Your App Description", "$1.99", "Your App Name");

            // initialize to empty product list in case debug IAP functionality is not needed
            string xml = @"<ProductListings></ProductListings>";

            var sri = App.GetResourceStream(new Uri("WindowsStoreProxy.xml", UriKind.Relative));
            if (sri != null)
            {
                System.Xml.Linq.XDocument doc = System.Xml.Linq.XDocument.Load(sri.Stream);
                xml = doc.ToString();
            }
            
            MockIAP.PopulateIAPItemsFromXml(xml);
#endif
        }

        // Intercept external links and open in the browser

        void Browser_Navigating(object sender, NavigatingEventArgs e)
        {
            String scheme = null;

            try
            {
                scheme = e.Uri.Scheme;
            }
            catch
            {
            }

            if (scheme == null || scheme == "file")
                return;
            // Not going to follow any other link
            e.Cancel = true;
            if (scheme == "http")
            {
                // start it in Internet Explorer
                WebBrowserTask webBrowserTask = new WebBrowserTask();
                webBrowserTask.Uri = new Uri(e.Uri.AbsoluteUri);
                webBrowserTask.Show();
            }
            if (scheme == "mailto")
            {
                EmailComposeTask emailComposeTask = new EmailComposeTask();
                emailComposeTask.To = e.Uri.AbsoluteUri;
                emailComposeTask.Show();
            }
        }

        // Handle navigation failures.

        private void Browser_NavigationFailed(object sender, System.Windows.Navigation.NavigationFailedEventArgs e)
        {
            MessageBox.Show("Navigation to this page failed, check your internet connection");
        }

        public void OnAppActivated()
        {
            Browser.InvokeScript("eval", "if (window.C2WP8Notify) C2WP8Notify('activated');");
            CheckLicense();
            checkMusic();
        }

        public void OnAppDeactivated()
        {
            Browser.InvokeScript("eval", "if (window.C2WP8Notify) C2WP8Notify('deactivated');");
        }

        // Convert DB volume to scale

        public float dbToScale(Single db)
        {
            float volume = (Single)Math.Pow(10, db / 20);
            volume = volume * (6 - (volume * 3)); // Added to account for quiet sounds being too quiet. Not a technically correct conversion.
            return Math.Max(0, Math.Min(1, volume));
        }

        public void storeListingRecieved()
        {
            if (productItems.Count > 0)
            {
                MemoryStream stream = new MemoryStream();
                DataContractJsonSerializer sr = new DataContractJsonSerializer(productItems.GetType());

                sr.WriteObject(stream, productItems);
                stream.Position = 0;

                StreamReader reader = new StreamReader(stream);
                string jsonResult = reader.ReadToEnd();

                // Pass listing obect back as a JavaScript object

                try
                {
                    Browser.InvokeScript("eval", "window['wp_call_OnStoreListing'](" + jsonResult + ");");
                }
                catch
                {
                }
            }
        }

        // Check whether user is playing music on the phones media player

        public void checkMusic()
        {
            FrameworkDispatcher.Update();

            if (MediaPlayer.GameHasControl)
            {
                try
                {
                    Browser.InvokeScript("eval", "window['deviceMusicPlaying'] = false;");
                }
                catch
                {
                }
            }
            else
            {
                try
                {
                    Browser.InvokeScript("eval", "window['deviceMusicPlaying'] = true;");
                }
                catch
                {
                }
            }
        }

        //Back button

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;

            try
            {
                Browser.InvokeScript("eval", "window['wp_call_OnBack']()");
            }
            catch
            {
                // Back function not available
            }
        }

        // Trial License

        private static Microsoft.Phone.Marketplace.LicenseInformation _licenseInfo = new Microsoft.Phone.Marketplace.LicenseInformation();

        private static bool _isTrial = true;
        public bool IsTrial
        {
            get
            {
                return _isTrial;
            }
        }
        public bool C2SettingIsTrial = false;

        private void CheckLicense()
        {

            if (C2SettingIsTrial)
            {
                //Trial
#if DEBUG
                _isTrial = true;
#else
                _isTrial = _licenseInfo.IsTrial();
#endif

                Browser.InvokeScript("eval", "window['isTrial'] = " + _isTrial.ToString().ToLower());

            }
        }

        // Sound effect instance

        SoundEffectInstance sound = null;
        public Dictionary<string, SoundEffectInstance> SoundList = new Dictionary<string, SoundEffectInstance>();

        // Product objects

        public class ProductItem
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string FormattedPrice { get; set; }
            public string Tag { get; set; }
            public string Purchased { get; set; }
        }

        public Dictionary<string, ProductItem> productItems = new Dictionary<string, ProductItem>();


        // **********************************************************************
        // JavaScript communicating with C#
        // **********************************************************************

        private async void Browser_ScriptNotify(object sender, NotifyEventArgs e)
        {


            // Get a comma delimited string from js and convert to array
            string valueStr = e.Value;
            string[] valueArr = valueStr.Split(',');

            // Trim and convert empty strings to null
            for (int i = 0; i < valueArr.Length; i++)
            {
                valueArr[i] = valueArr[i].Trim();
                if (string.IsNullOrWhiteSpace(valueArr[i]))
                    valueArr[i] = null;
            }

            // Activate trial mode
            if (valueArr[0] == "checkLicense")
            {

                // Check if trial
                if (valueArr[1] == "true")
                {
                    C2SettingIsTrial = true;
                }
                CheckLicense();
            }

            // Game loaded
            if (valueArr[0] == "gameLoaded")
            {
                Browser.Visibility = System.Windows.Visibility.Visible;

                checkMusic();

                if (App.WasTombstoned)
                {
                    Browser.InvokeScript("eval", "window['tombstoned'] = true");
                    App.WasTombstoned = false;
                }
            }

            // Stop music
            if (valueArr[0] == "stopMusic")
            {
                if (MediaPlayer.GameHasControl)
                {
                    FrameworkDispatcher.Update();
                    MediaPlayer.Stop();
                }
            }

            // Play music
            if (valueArr[0] == "playMusic")
            {
                if (MediaPlayer.GameHasControl)
                {
                    var file = valueArr[1];
                    var loop = valueArr[2] == "0" ? false : true;

                    var db = Single.Parse(valueArr[3], CultureInfo.InvariantCulture);
                    var volume = Convert.ToSingle(dbToScale(db));

                    var uri = new Uri(file, UriKind.Relative);
                    var song = Song.FromUri(file, uri);
                    MediaPlayer.IsRepeating = loop;
                    MediaPlayer.Volume = volume;
                    FrameworkDispatcher.Update();
                    MediaPlayer.Stop();
                    MediaPlayer.Play(song);
                }
            }

            // Stop music
            if (valueArr[0] == "stopMusic")
            {
                if (MediaPlayer.GameHasControl)
                {
                    FrameworkDispatcher.Update();
                    MediaPlayer.Stop();
                }
            }

            // Play sound
            if (valueArr[0] == "playSound")
            {

                var file = valueArr[1];
                var loop = valueArr[2] == "0" ? false : true;

                var db = Single.Parse(valueArr[3], CultureInfo.InvariantCulture);
                var volume = Convert.ToSingle(dbToScale(db));

                var tag = valueArr[4];

                Stream stream = TitleContainer.OpenStream(file);
                SoundEffect effect = SoundEffect.FromStream(stream);

                sound = effect.CreateInstance();
                sound.IsLooped = loop;
                sound.Volume = volume;

                if (!string.IsNullOrEmpty(tag))
                {
                    if (SoundList.ContainsKey(tag))
                    {
                        SoundList[tag].Stop();
                    }
                    SoundList[tag] = sound;
                }

                FrameworkDispatcher.Update();
                sound.Play();

            }

            // Stop sound
            if (valueArr[0] == "stopSound")
            {
                var tag = valueArr[1];
                if (SoundList.ContainsKey(tag))
                {
                    FrameworkDispatcher.Update();
                    SoundList[tag].Stop();
                }
            }

            // Vibrate
            if (valueArr[0] == "vibrate")
            {
                float seconds = float.Parse(valueArr[1], CultureInfo.InvariantCulture);
                VibrateController vibrate = VibrateController.Default;
                vibrate.Start(TimeSpan.FromSeconds(seconds));
            }

            // Quit app
            if (valueArr[0] == "quitApp")
            {
                App.Current.Terminate();
            }

            // Live Tiles (http://tinyurl.com/afvhgz8)
            // *******************************************************

            // Flipped Tile
            if (valueArr[0] == "flippedTileUpdate")
            {
                ShellTile myTile = ShellTile.ActiveTiles.First();
                if (myTile != null)
                {
                    var smallBackgroundImage = valueArr[6] == null ? null : new Uri(valueArr[6], UriKind.Relative);
                    var backgroundImage = valueArr[7] == null ? null : new Uri(valueArr[7], UriKind.Relative);
                    var backBackgroundImage = valueArr[8] == null ? null : new Uri(valueArr[8], UriKind.Relative);
                    var wideBackgroundImage = valueArr[9] == null ? null : new Uri(valueArr[9], UriKind.Relative);
                    var wideBackBackgroundImage = valueArr[10] == null ? null : new Uri(valueArr[10], UriKind.Relative);

                    FlipTileData newTileData = new FlipTileData
                    {
                        Title = valueArr[1],
                        BackTitle = valueArr[2],
                        BackContent = valueArr[3],
                        WideBackContent = valueArr[4],
                        Count = Convert.ToInt32(valueArr[5]),
                        SmallBackgroundImage = smallBackgroundImage,
                        BackgroundImage = backgroundImage,
                        BackBackgroundImage = backBackgroundImage,
                        WideBackgroundImage = wideBackgroundImage,
                        WideBackBackgroundImage = wideBackBackgroundImage
                    };
                    myTile.Update(newTileData);
                }
            }

            // Payments

            // Purchase app
            if (valueArr[0] == "purchaseApp")
            {
                MarketplaceDetailTask _marketPlaceDetailTask = new MarketplaceDetailTask();
                _marketPlaceDetailTask.Show();
            }

            // Purchase product
            if (valueArr[0] == "purchaseProduct")
            {
                string productID = valueArr[1];

                if (!CurrentApp.LicenseInformation.ProductLicenses[productID].IsActive)
                {
                    try
                    {
                        var receipt = await CurrentApp.RequestProductPurchaseAsync(productID, true);
                        if (CurrentApp.LicenseInformation.ProductLicenses[productID].IsActive)
                        {
                            Browser.InvokeScript("eval", "window['wp_call_IAPPurchaseSuccess']('" + productID + "');");
                        }
                    }
                    catch
                    {
                        // The in-app purchase was not completed because the
                        // customer canceled it or an error occurred.
                        Browser.InvokeScript("eval", "window['wp_call_IAPPurchaseFail']();");
                    }
                }
                else
                {
                    //Already owns the product
                }
            }

            // Request store listing
            if (valueArr[0] == "requestStoreListing")
            {
                try
                {

                    li = await Store.CurrentApp.LoadListingInformationAsync();

                    foreach (string key in li.ProductListings.Keys)
                    {
                        ProductListing pListing = li.ProductListings[key];

                        productItems[pListing.ProductId] = new ProductItem
                        {
                            Name = pListing.Name,
                            Description = pListing.Description,
                            FormattedPrice = pListing.FormattedPrice,
                            Tag = pListing.Tag,
                            Purchased = CurrentApp.LicenseInformation.ProductLicenses[key].IsActive ? "True" : "False"
                        };
                    }

                    storeListingRecieved();
                }
                catch (Exception)
                {
                    // Failed to load listing information
                }
            }

            // Rate App
            if (valueArr[0] == "rateApp")
            {
                MarketplaceReviewTask marketplaceReviewTask = new MarketplaceReviewTask();
                marketplaceReviewTask.Show();
            }

        }
    }
}