using GemiNaut.Singletons;
using GemiNaut.Views;
using System;
using System.IO;
using System.Windows.Controls;
using static GemiNaut.Views.MainWindow;

namespace GemiNaut
{
    public class AboutNavigator
    {
        private readonly MainWindow mMainWindow;

        public AboutNavigator(MainWindow window)
        {
            mMainWindow = window;
        }

        public bool NavigateAboutScheme(Uri uri, SiteIdentity siteIdentity)
        {
            var sessionPath = Session.Instance.SessionPath;
            var appDir = System.AppDomain.CurrentDomain.BaseDirectory;

            string fullQuery;
            //just load the help file
            //no further action
            mMainWindow.ToggleContainerControlsForBrowser(true);

            var sourceFileName = uri.PathAndQuery.Substring(1);      //trim off leading /

            //this expects uri has a "geminaut" domain so gmitohtml converter can proceed for now
            //I think it requires a domain for parsing...
            fullQuery = uri.OriginalString;

            var hash = HashService.GetMd5Hash(fullQuery);

            var hashFile = Path.Combine(sessionPath, hash + ".txt");
            var htmlCreateFile = Path.Combine(sessionPath, hash + ".htm");

            var helpFolder = ResourceFinder.LocalOrDevFolder(appDir, @"Docs", @"..\..\..\Docs");
            var helpFile = Path.Combine(helpFolder, sourceFileName);

            //use a specific theme so about pages look different to user theme
            var templateBaseName = Path.Combine(helpFolder, "help-theme");

            if (File.Exists(helpFile))
            {
                File.Copy(helpFile, hashFile, true);
                mMainWindow.ShowUrl(fullQuery, hashFile, htmlCreateFile, templateBaseName, siteIdentity);
            }
            else
            {
                mMainWindow.ToastNotify("No content was found for: " + fullQuery, ToastMessageStyles.Warning);
                return false;
            }

            return true;
        }
    }
}
