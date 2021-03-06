﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.IO;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Position;
using ToastNotifications.Messages;
using System.Diagnostics;
using GemiNaut.Singletons;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using GemiNaut.Properties;
using System.Xml.Linq;
using System.Linq;
using TheArtOfDev.HtmlRenderer.WPF;
using TheArtOfDev.HtmlRenderer.Core.Entities;

namespace GemiNaut.Views
{
    public partial class MainWindow : Window
    {
        private static readonly Settings _settings = new Settings();
        private readonly Dictionary<string, string> _urlsByHash;
        private readonly Notifier _notifier;
        private readonly BookmarkManager _bookmarkManager;
        private bool _isNavigating;

        public MainWindow()
        {
            InitializeComponent();

            BrowserControl.LinkClicked += HtmlPanel_LinkClicked;

            _notifier = new Notifier(cfg =>
            {
                //place the notifications approximately inside the main editing area
                //(not over the toolbar area) on the top-right hand side
                cfg.PositionProvider = new WindowPositionProvider(
                    parentWindow: Application.Current.MainWindow,
                    corner: Corner.TopRight,
                    offsetX: 15,
                    offsetY: 90);

                cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                    notificationLifetime: TimeSpan.FromSeconds(5),
                    maximumNotificationCount: MaximumNotificationCount.FromCount(5));

                cfg.Dispatcher = Application.Current.Dispatcher;
            });

            _urlsByHash = new Dictionary<string, string>();
            _bookmarkManager = new BookmarkManager(this, BrowserControl);

            AppInit.UpgradeSettings();
            AppInit.CopyAssets();

            _bookmarkManager.RefreshBookmarkMenu();

            var launchUri = _settings.HomeUrl;

            string[] args = App.Args;
            if (args != null)
            {
                launchUri = App.Args[0];
            }

            Navigate(launchUri);

            BuildThemeMenu();
            TickSelectedThemeMenu();
        }

        private void HtmlPanel_LinkClicked(object sender, RoutedEventArgs<HtmlLinkClickedEventArgs> args)
        {
            Navigate(args.Data.Link);
        }

        private void TxtUrl_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (UriTester.TextIsUri(txtUrl.Text))
                {
                    Navigate(txtUrl.Text);
                }
                else
                {
                    ToastNotify("Not a valid URI: " + txtUrl.Text, ToastMessageStyles.Error);
                }
            }
        }

        public void ToggleContainerControlsForBrowser(bool toState)
        {
            //we need to turn off other elements so focus doesnt move elsewhere
            //in that case the keyboard events go elsewhere and you have to click 
            //into the browser to get it to work again
            //see https://stackoverflow.com/questions/8495857/webbrowser-steals-focus

            TopDock.IsEnabled = toState;
            //DockMenu.IsEnabled = toState;     //not necessary as it is not a container for the webbrowser
            DockLower.IsEnabled = toState;
            GridMain.IsEnabled = toState;
        }

        public void ShowImage(string sourceUrl, string imgFile)
        {
            var hash = HashService.GetMd5Hash(sourceUrl);

            _urlsByHash[hash] = sourceUrl;

            //instead tell the browser to load the content
            Navigate(@"file:///" + imgFile);
        }

        public void ShowUrl(string sourceUrl, string gmiFile, string htmlFile, string themePath, SiteIdentity siteIdentity)
        {
            var hash = HashService.GetMd5Hash(sourceUrl);

            var usedShowWebHeaderInfo = false;

            var uri = new UriBuilder(sourceUrl);

            //only show web header for self generated content, not proxied
            usedShowWebHeaderInfo = uri.Scheme.StartsWith("http") && _settings.HandleWebLinks != "Gemini HTTP proxy";

            //create the html file
            ConverterService.CreateDirectoriesIfNeeded(gmiFile, htmlFile, themePath);
            var result = ConverterService.GmiToHtml(gmiFile, htmlFile, sourceUrl, siteIdentity, themePath, usedShowWebHeaderInfo);

            if (!File.Exists(htmlFile))
            {
                ToastNotify("GMIToHTML did not create content for " + sourceUrl + "\n\nFile: " + gmiFile, ToastMessageStyles.Error);

                ToggleContainerControlsForBrowser(true);
            }
            else
            {
                _urlsByHash[hash] = sourceUrl;

                //instead tell the browser to load the content
                Navigate(@"file:///" + htmlFile);
            }
        }

        public enum ToastMessageStyles
        {
            Information, Warning, Error,
            Success
        }

        public void ToastNotify(string message, ToastMessageStyles style)
        {
            try
            {
                if (style == ToastMessageStyles.Information) { _notifier.ShowInformation(message); }
                if (style == ToastMessageStyles.Error) { _notifier.ShowError(message); }
                if (style == ToastMessageStyles.Success) { _notifier.ShowSuccess(message); }
                if (style == ToastMessageStyles.Warning) { _notifier.ShowWarning(message); }
            }
            catch
            {
                //for example main window might not be visible yet, so just ignore those.
            }
        }

        //simple overload method without style
        public void ToastNotify(string message)
        {
            ToastNotify(message, ToastMessageStyles.Information);
        }

        private void BrowseBack_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            // e.CanExecute = ((BrowserControl != null) && (BrowserControl.CanGoBack));
        }

        private void BrowseBack_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            // BrowserControl.GoBack();
        }

        private void BrowseHome_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (BrowserControl != null);
        }

        private void BrowseHome_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Navigate(_settings.HomeUrl);
        }

        private void BrowseForward_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            // e.CanExecute = ((BrowserControl != null) && (BrowserControl.CanGoForward));
        }

        private void BrowseForward_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            // BrowserControl.GoForward();
        }

        private void GoToPage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !_isNavigating;
        }

        private void GoToPage_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (UriTester.TextIsUri(txtUrl.Text))
            {
                Navigate(txtUrl.Text);
            }
            else
            {
                ToastNotify("Not a valid URI: " + txtUrl.Text, ToastMessageStyles.Error);
            }
        }

        private void MenuFileExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MenuHelpAbout_Click(object sender, RoutedEventArgs e)
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;

            var message = "GemiNaut v" + version + ", copyright Luke Emmet 2020\n";

            ToastNotify(message, ToastMessageStyles.Information);
        }

        private void MenuHelpContents_Click(object sender, RoutedEventArgs e)
        {
            //this will be a look up into docs folder
            Navigate("about://geminaut/help.gmi");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //shutdown and dispose of session singleton
            Session.Instance.Dispose();
            Application.Current.Shutdown();
        }

        private void MenuViewSource_Click(object sender, RoutedEventArgs e)
        {
            var menu = (MenuItem)sender;
            string hash;

            //use the current session folder
            var sessionPath = Session.Instance.SessionPath;

            hash = HashService.GetMd5Hash(txtUrl.Text);

            //uses .txt as extension so content loaded as text/plain not interpreted by the browser
            var gmiFile = sessionPath + "\\" + hash + ".txt";

            Navigate(gmiFile);
        }

        private void MenuViewSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsEditor = new SettingsEditor();
            settingsEditor.Owner = this;
            settingsEditor.ShowDialog();
        }

        //decorate links that switch the mode so the current mode is highlighted
        private static void ShowLinkRenderMode(string html)
        {
            var doc = XDocument.Parse(html);

            //decrate the current mode
            var modeLink = doc.Descendants(_settings.WebRenderMode).First();
            if (modeLink != null)
            {
                modeLink.Value = "[" + modeLink.Value + "]";
                modeLink.SetAttributeValue("FontWeight", "bold");
            }
        }

        private void ShowTitle(HtmlPanel panel)
        {
            //update title, this might fail when called from Navigated as the document might not be ready yet
            //but we also call on LoadCompleted. This should catch both situations
            //of real navigation and also back and forth in history

            var title = panel.GetTitle();

            const string geminiTitle = "GemiNaut, a friendly GUI browser";

            try
            {
                Application.Current.MainWindow.Title = title == null ? geminiTitle : title + " - " + geminiTitle;
            }
            catch (Exception e)
            {
                ToastNotify(e.Message, ToastMessageStyles.Error);
            }
        }

        private void BuildThemeMenu()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            var themeFolder = ResourceFinder.LocalOrDevFolder(appDir, @"GmiConverters\themes", @"..\..\GmiConverters\themes");

            foreach (var file in Directory.EnumerateFiles(themeFolder, "*.htm"))
            {
                var newMenu = new MenuItem();
                newMenu.Header = Path.GetFileNameWithoutExtension(Path.Combine(themeFolder, file));
                newMenu.Click += ViewThemeItem_Click;

                mnuTheme.Items.Add(newMenu);
            }
        }

        private void TickSelectedThemeMenu()
        {
            foreach (MenuItem themeMenu in mnuTheme.Items)
            {
                themeMenu.IsChecked = (themeMenu.Header.ToString() == _settings.Theme);
            }
        }

        private void ViewThemeItem_Click(object sender, RoutedEventArgs e)
        {
            var menu = (MenuItem)sender;
            var themeName = menu.Header.ToString();

            if (_settings.Theme != themeName)
            {
                _settings.Theme = themeName;
                _settings.Save();

                TickSelectedThemeMenu();

                //redisplay
                Navigate(txtUrl.Text);
            }
        }

        private void mnuMenuBookmarksAdd_Click(object sender, RoutedEventArgs e)
        {
            var bmManager = new BookmarkManager(this, BrowserControl);
            bmManager.AddBookmark(txtUrl.Text, BrowserControl.GetTitle());
            var url = txtUrl.Text;
        }

        private void mnuMenuBookmarksEdit_Click(object sender, RoutedEventArgs e)
        {
            Bookmarks winBookmarks = new Bookmarks(this, BrowserControl);

            //show modally
            winBookmarks.Owner = this;
            winBookmarks.ShowDialog();
        }

        public void mnuMenuBookmarksGo_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;

            Navigate(menuItem.CommandParameter.ToString());
        }

        private void MenuFileNew_Click(object sender, RoutedEventArgs e)
        {
            //start a completely new GemiNaut session, with the current URL
            var location = System.Reflection.Assembly.GetEntryAssembly().Location;

            var info = new ProcessStartInfo()
            {
                FileName = "dotnet",
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(location),
                ArgumentList = { Path.GetFileName(location), txtUrl.Text }
            };

            Process.Start(info);
        }

        public void Navigate(string uriString)
        {
            _isNavigating = true;

            bool cancelled = false;

            var uri = new Uri(uriString);

            var normalisedUri = UriTester.NormaliseUri(uri);

            var siteIdentity = new SiteIdentity(normalisedUri, Session.Instance);

            var fullQuery = normalisedUri.OriginalString;

            //sanity check we have a valid URL syntax at least
            if (normalisedUri.Scheme == null)
            {
                ToastNotify("Invalid URL: " + normalisedUri.OriginalString, ToastMessageStyles.Error);
                cancelled = true;
            }

            ToggleContainerControlsForBrowser(false);

            //these are the only ones we "navigate" to. We do this by downloading the GMI content
            //converting to HTML and then actually navigating to that.
            if (normalisedUri.Scheme == "gemini")
            {
                var geminiNavigator = new GeminiNavigator(this);
                cancelled = geminiNavigator.NavigateGeminiScheme(fullQuery, uri, siteIdentity);
            }
            else if (normalisedUri.Scheme == "gopher")
            {
                var gopherNavigator = new GopherNavigator(this);
                cancelled = gopherNavigator.NavigateGopherScheme(fullQuery, uri, siteIdentity);
            }
            else if (normalisedUri.Scheme == "about")
            {
                var aboutNavigator = new AboutNavigator(this);
                cancelled = aboutNavigator.NavigateAboutScheme(uri, siteIdentity);
            }
            else if (normalisedUri.Scheme.StartsWith("http"))       //both http and https
            {
                //detect ctrl click
                if (_settings.HandleWebLinks == "System web browser")
                {
                    //open in system web browser
                    var launcher = new ExternalNavigator(this);
                    launcher.LaunchExternalUri(uri.ToString());
                    ToggleContainerControlsForBrowser(true);
                    cancelled = true;
                }
                else if (_settings.HandleWebLinks == "Gemini HTTP proxy")
                {
                    // use a gemini proxy for http links
                    var geminiNavigator = new GeminiNavigator(this);
                    cancelled = geminiNavigator.NavigateGeminiScheme(fullQuery, uri, siteIdentity);
                }
                else
                {
                    //use internal navigator
                    var httpNavigator = new HttpNavigator(this);
                    cancelled = httpNavigator.NavigateHttpScheme(fullQuery, uri, siteIdentity, "web-launch-external");
                }
            }
            else if (normalisedUri.Scheme == "file")
            {
                //just load the converted html file
                //no further action.

                BrowserControl.Text = File.ReadAllText(normalisedUri.LocalPath);
            }
            else
            {
                //we don't care about any other protocols
                //so we open those in system web browser to deal with
                var launcher = new ExternalNavigator(this);
                launcher.LaunchExternalUri(uri.ToString());
                ToggleContainerControlsForBrowser(true);
                cancelled = true;
            }

            if (!cancelled)
            {
                uriString = uri.ToString();

                //look up the URL that this HTML page shows
                var regex = new Regex(@".*/([a-f0-9]+)\.(.*)");
                if (regex.IsMatch(uriString))
                {
                    var match = regex.Match(uriString);
                    var hash = match.Groups[1].ToString();

                    string geminiUrl = _urlsByHash[hash];
                    if (geminiUrl != null)
                    {
                        //now show the actual gemini URL in the address bar
                        txtUrl.Text = geminiUrl;

                        ShowTitle(BrowserControl);

                        var originalUri = new UriBuilder(geminiUrl);

                        if (originalUri.Scheme == "http" || originalUri.Scheme == "https")
                        {
                            ShowLinkRenderMode(BrowserControl.Text);
                        }
                    }

                    //if a text file (i.e. view->source), explicitly set the charset
                    //to UTF-8 so ascii art looks correct etc.
                    if ("txt" == match.Groups[2].ToString().ToLower())
                    {
                        //set text files (GMI source) to be UTF-8 for now
                        HtmlPanelExtensions.SetCharsetUtf8(BrowserControl.Text);
                    }
                }

                BrowserControl.Focus();

                ShowTitle(BrowserControl);

                //we need to turn on/off other elements so focus doesnt move elsewhere
                //in that case the keyboard events go elsewhere and you have to click 
                //into the browser to get it to work again
                //see https://stackoverflow.com/questions/8495857/webbrowser-steals-focus
                ToggleContainerControlsForBrowser(true);
            }

            _isNavigating = false;
        }
    }
}
