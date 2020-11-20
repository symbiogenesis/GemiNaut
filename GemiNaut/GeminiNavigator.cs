﻿//===================================================
//    GemiNaut, a friendly browser for Gemini space on Windows

//    Copyright (C) 2020, Luke Emmet 

//    Email: luke [dot] emmet [at] gmail [dot] com

//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.

//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.

//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <https://www.gnu.org/licenses/>.
//===================================================

using GemiNaut.Properties;
using GemiNaut.Serialization.Commandline;
using GemiNaut.Singletons;
using GemiNaut.Views;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.IO;
using TheArtOfDev.HtmlRenderer.WPF;
using static GemiNaut.Views.MainWindow;

namespace GemiNaut
{
    public class GeminiNavigator
    {
        private readonly MainWindow mMainWindow;

        public GeminiNavigator(MainWindow mainWindow)
        {
            mMainWindow = mainWindow;
        }

        public bool NavigateGeminiScheme(string fullQuery, Uri uri, SiteIdentity siteIdentity, bool requireSecure = true)
        {
            bool navigated = true;

            var geminiUri = uri.OriginalString;

            var sessionPath = Session.Instance.SessionPath;
            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            //use local or dev binary for gemget
            var gemGet = ResourceFinder.LocalOrDevFile(appDir, "Gemget", "..\\..\\..\\..\\Gemget", "gemget-windows-386.exe");

            var hash = HashService.GetMd5Hash(fullQuery);

            //uses .txt as extension so content loaded as text/plain not interpreted by the browser
            //if user requests a view-source.
            var rawFile = sessionPath + "\\" + hash + ".txt";
            var gmiFile = sessionPath + "\\" + hash + ".gmi";
            var htmlFile = sessionPath + "\\" + hash + ".htm";

            //delete txt file as GemGet seems to sometimes overwrite not create afresh
            File.Delete(rawFile);
            File.Delete(gmiFile);

            //delete any existing html file to encourage webbrowser to reload it
            File.Delete(htmlFile);

            var settings = new Settings();
            string command = "";

            var secureFlag = requireSecure ? "" : " -i ";

            if (uri.Scheme == "gemini")
            {
                //pass options to gemget for download
                command = string.Format(
                    "\"{0}\" {1} --header --no-progress-bar -m \"{2}\"Mb -t {3} -o \"{4}\" \"{5}\"",
                    gemGet,
                    secureFlag,
                    settings.MaxDownloadSizeMb,
                    settings.MaxDownloadTimeSeconds,
                    rawFile,
                    fullQuery);
            }
            else
            {
                //pass options to gemget for download using the assigned http proxy, such as 
                //duckling-proxy https://github.com/LukeEmmet/duckling-proxy
                //this should obviously be a trusted server since it is in the middle of the 
                //request
                command = string.Format(
                    "\"{0}\" {1} --header --no-progress-bar -m \"{2}\"Mb -t {3} -o \"{4}\"  -p \"{5}\" \"{6}\"",
                    gemGet,
                    secureFlag,
                    settings.MaxDownloadSizeMb,
                    settings.MaxDownloadTimeSeconds,
                    rawFile,
                    settings.HttpSchemeProxy,
                    fullQuery);
            }

            var result = ExecuteProcess.ExecuteCommand(command, true, true);

            var geminiResponse = new Response.GeminiResponse(fullQuery);

            geminiResponse.ParseGemGet(result.Item2);   //parse stdout   
            geminiResponse.ParseGemGet(result.Item3);   //parse stderr

            //ToastNotify(geminiResponse.Status + " " + geminiResponse.Meta);

            //in these early days of Gemini we dont forbid visiting a site with an expired cert or mismatched host name
            //but we do give a warning each time
            if (result.Item1 == 1 && requireSecure)
            {
                var tryInsecure = false;
                var securityError = "";
                if (geminiResponse.Errors[0].Contains("server cert is expired"))
                {
                    tryInsecure = true;
                    securityError = "Server certificate is expired";
                }
                else if (geminiResponse.Errors[0].Contains("hostname does not verify"))
                {
                    tryInsecure = true;
                    securityError = "Host name does not verify";
                }
                if (tryInsecure)
                {
                    //give a warning and try again with insecure
                    mMainWindow.ToastNotify("Note: " + securityError + " for: " + uri.Authority, ToastMessageStyles.Warning);
                    NavigateGeminiScheme(fullQuery, uri, siteIdentity, false);
                    return true;
                }
            }

            if (geminiResponse.AbandonedTimeout || geminiResponse.AbandonedSize)
            {
                var abandonMessage = string.Format(
                        "Download was abandoned as it exceeded the max size ({0}) or time ({1} s). See GemiNaut settings for details.\n\n{2}",
                        settings.MaxDownloadSizeMb,
                        settings.MaxDownloadTimeSeconds,
                        fullQuery);

                mMainWindow.ToastNotify(abandonMessage, ToastMessageStyles.Warning);
                mMainWindow.ToggleContainerControlsForBrowser(true);
                return false;
            }

            if (File.Exists(rawFile))
            {
                if (geminiResponse.Meta.Contains("text/gemini"))
                {
                    File.Copy(rawFile, gmiFile);
                }
                else if (geminiResponse.Meta.Contains("text/html"))
                {
                    //is an html file served over gemini - probably not common, but not unheard of
                    var htmltoGmiResult = ConverterService.HtmlToGmi(rawFile, gmiFile);

                    if (htmltoGmiResult.Item1 != 0)
                    {
                        mMainWindow.ToastNotify("Could not convert HTML to GMI: " + fullQuery, ToastMessageStyles.Error);
                        mMainWindow.ToggleContainerControlsForBrowser(true);
                        return false;
                    }
                }
                else if (geminiResponse.Meta.Contains("text/"))
                {
                    //convert plain text to a gemini version (wraps it in a preformatted section)
                    var textToGmiResult = ConverterService.TextToGmi(rawFile, gmiFile);

                    if (textToGmiResult.Item1 != 0)
                    {
                        mMainWindow.ToastNotify("Could not render text as GMI: " + fullQuery, ToastMessageStyles.Error);
                        mMainWindow.ToggleContainerControlsForBrowser(true);
                        return false;
                    }
                }
                else
                {
                    //a download
                    //its an image - rename the raw file and just show it
                    var pathFragment = (new UriBuilder(fullQuery)).Path;
                    var ext = Path.GetExtension(pathFragment);

                    var binFile = rawFile + (ext == "" ? ".tmp" : ext);
                    File.Copy(rawFile, binFile, true); //rename overwriting

                    if (geminiResponse.Meta.Contains("image/"))
                    {
                        mMainWindow.ShowImage(fullQuery, binFile);
                    }
                    else
                    {
                        SaveFileDialog saveFileDialog = new SaveFileDialog();

                        saveFileDialog.FileName = Path.GetFileName(pathFragment);

                        if (saveFileDialog.ShowDialog() == true)
                        {
                            try
                            {
                                //save the file
                                var savePath = saveFileDialog.FileName;

                                File.Copy(binFile, savePath, true); //rename overwriting

                                mMainWindow.ToastNotify("File saved to " + savePath, ToastMessageStyles.Success);
                            }
                            catch (SystemException err)
                            {
                                mMainWindow.ToastNotify("Could not save the file due to: " + err.Message, ToastMessageStyles.Error);
                            }
                        }

                        mMainWindow.ToggleContainerControlsForBrowser(true);
                        return false;
                    }

                    return true;
                }

                if (geminiResponse.Redirected)
                {
                    string redirectUri = fullQuery;

                    if (geminiResponse.FinalUrl.Contains("://"))
                    {
                        //a full url
                        //normalise the URi (e.g. remove default port if specified)
                        redirectUri = UriTester.NormaliseUri(new Uri(geminiResponse.FinalUrl)).ToString();
                    }
                    else
                    {
                        //a relative one
                        var baseUri = new Uri(fullQuery);
                        var targetUri = new Uri(baseUri, geminiResponse.FinalUrl);
                        redirectUri = UriTester.NormaliseUri(targetUri).ToString();
                    }

                    var finalUri = new Uri(redirectUri);

                    if (uri.Scheme == "gemini" && finalUri.Scheme != "gemini")
                    {
                        //cross-scheme redirect, not supported
                        mMainWindow.ToastNotify("Cross scheme redirect from Gemini not supported: " + redirectUri, ToastMessageStyles.Warning);
                        mMainWindow.ToggleContainerControlsForBrowser(true);
                        return false;
                    }
                    else
                    {
                        //others e.g. http->https redirect are fine
                    }

                    //redirected to a full gemini url
                    geminiUri = redirectUri;

                    //regenerate the hashes using the redirected target url
                    hash = HashService.GetMd5Hash(geminiUri);

                    var gmiFileNew = sessionPath + "\\" + hash + ".txt";
                    var htmlFileNew = sessionPath + "\\" + hash + ".htm";

                    //move the source file
                    try
                    {
                        if (File.Exists(gmiFileNew))
                        {
                            File.Delete(gmiFileNew);
                        }
                        File.Move(gmiFile, gmiFileNew);
                    }
                    catch (Exception err)
                    {
                        mMainWindow.ToastNotify(err.ToString(), ToastMessageStyles.Error);
                    }

                    //update locations of gmi and html file
                    gmiFile = gmiFileNew;
                    htmlFile = htmlFileNew;
                }
                else
                {
                    geminiUri = fullQuery;
                }

                var userThemesFolder = ResourceFinder.LocalOrDevFolder(appDir, @"GmiConverters\themes", @"..\..\GmiConverters\themes");

                var userThemeBase = Path.Combine(userThemesFolder, settings.Theme);

                mMainWindow.ShowUrl(geminiUri, gmiFile, htmlFile, userThemeBase, siteIdentity);
            }
            else if (geminiResponse.Status == 10 || geminiResponse.Status == 11)
            {
                //needs input

                mMainWindow.ToggleContainerControlsForBrowser(true);

                navigated = NavigateGeminiWithInput(uri, geminiResponse.Meta);
            }
            else if (geminiResponse.Status == 50 || geminiResponse.Status == 51)
            {
                mMainWindow.ToastNotify("Page not found (status 51)\n\n" + uri.ToString(), ToastMessageStyles.Warning);
            }
            else
            {
                //some othe error - show to the user for info
                mMainWindow.ToastNotify(string.Format(
                    "Cannot retrieve the content (exit code {0}): \n\n{1} \n\n{2}",
                    result.Item1,
                    string.Join("\n\n", geminiResponse.Info),
                    string.Join("\n\n", geminiResponse.Errors)
                    ),
                    ToastMessageStyles.Error);
            }

            mMainWindow.ToggleContainerControlsForBrowser(true);

            //no further navigation right now
            return navigated;
        }

        //navigate to a url but get some user input first
        public bool NavigateGeminiWithInput(Uri uri, string message)
        {
            //position input box approx in middle of main window

            var windowCentre = WindowGeometry.WindowCentre(mMainWindow);
            var inputPrompt = "Input request from Gemini server\n\n" +
                "  " + uri.Host + uri.LocalPath + "\n\n" +
                message;

            string input = Interaction.InputBox(inputPrompt, "Server input request", "", windowCentre.Item1, windowCentre.Item2);

            if (input != "")
            {
                //encode the query
                var b = new UriBuilder();
                b.Scheme = uri.Scheme;
                b.Host = uri.Host;
                if (uri.Port != -1) { b.Port = uri.Port; }
                b.Path = uri.LocalPath;
                //!%22%C2%A3$%25%5E&*()_+1234567890-=%7B%7D:@~%3C%3E?[];'#,./
                b.Query = Uri.EscapeDataString(input);      //escape the query result

                //ToastNotify(b.ToString());

                mMainWindow.Navigate(b.ToString());
            }
            else
            {
                //dont do anything further with navigating the browser
                return false;
            }

            return true;
        }
    }
}
