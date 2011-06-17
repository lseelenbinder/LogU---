//------------------------------------------------------------------------------------
// Program: LogU/\/
// Revision: 1.0
// Date: 08/17/2010
// This program was created to interface with the BJU proxy management system, 
// allowing window's users to automatically log themselves in.
// ~bulletshot60
//------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Net.Security;
using Microsoft.Win32;

namespace LogUIN
{
    public partial class MainForm : Form
    {
        private static double version = 2.0;
        private static string encrypt_key = "d|?????";
        public bool logged_in, thread_completed;
        string file_path = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) + "Documents and Settings\\" + System.Windows.Forms.SystemInformation.UserName.ToString() + "\\Local Settings\\Application Data\\loguin_user_data.txt";
        string temp_path = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) + "Documents and Settings\\" + System.Windows.Forms.SystemInformation.UserName.ToString() + "\\Local Settings\\Application Data\\temp.txt";
        string global_username, global_password;
        Thread service_thread;

        public MainForm()
        {
            InitializeComponent();
            //attempt to load user profile from encryped file located
            //in application_settings
            load_user_information();
            //setup variables
            logged_in = false;
            //start user off logged out
            logoutToolStripMenuItem.Enabled = false;
            thread_completed = true;
            SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(PowerModeChanged);
            //set up application
            //set value so program will autostart on next boot
            Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce", true);
            key.SetValue("BorderManagerLogin", Application.ExecutablePath.ToString());
            try
            {
                key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                key.DeleteValue("BorderManagerLogin");
            }
            catch (Exception e)
            {
                ;
            }
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
        }

        static void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                //Computer waking from sleep or hibernate, reset application,
                //perform cleanup, and then log user back in
                Application.Restart();
                SystemEvents.PowerModeChanged -= new PowerModeChangedEventHandler(PowerModeChanged);
            }
        }

        static void DecryptFile(string input_filename, string output_filename)
        {
            DESCryptoServiceProvider DES = new DESCryptoServiceProvider();
            DES.Key = ASCIIEncoding.ASCII.GetBytes(encrypt_key);
            DES.IV = ASCIIEncoding.ASCII.GetBytes(encrypt_key);
            FileStream fsread = new FileStream(input_filename, FileMode.Open, FileAccess.Read);
            ICryptoTransform desdecrypt = DES.CreateDecryptor();
            CryptoStream cryptostreamDecr = new CryptoStream(fsread, desdecrypt, CryptoStreamMode.Read);
            StreamWriter fsDecrypted = new StreamWriter(output_filename);
            fsDecrypted.Write(new StreamReader(cryptostreamDecr).ReadToEnd());
            fsDecrypted.Flush();
            fsDecrypted.Close();
            fsread.Close();
        }
        
        static void EncryptFile(string input_filename, string output_filename)
        {
            FileStream fsInput = new FileStream(input_filename, FileMode.Open, FileAccess.Read);
            FileStream fsEncrypted = new FileStream(output_filename, FileMode.Create, FileAccess.Write);
            DESCryptoServiceProvider DES = new DESCryptoServiceProvider();
            DES.Key = ASCIIEncoding.ASCII.GetBytes(encrypt_key);
            DES.IV = ASCIIEncoding.ASCII.GetBytes(encrypt_key);
            ICryptoTransform desencrypt = DES.CreateEncryptor();
            CryptoStream cryptostream = new CryptoStream(fsEncrypted, desencrypt, CryptoStreamMode.Write);
            byte[] bytearrayinput = new byte[fsInput.Length];
            fsInput.Read(bytearrayinput, 0, bytearrayinput.Length);
            cryptostream.Write(bytearrayinput, 0, bytearrayinput.Length);
            cryptostream.Close();
            fsInput.Close();
            fsEncrypted.Close();
        }

        //Request the update file from the website to determine if a new version of LogU/\/ exists.
        private bool check_for_updates()
        {
            try
            {
                //Request current version file from website
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://bulletshot60.web.officelive.com/updates.html");
                request.Method = "GET";

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                StreamReader reader = new StreamReader(response.GetResponseStream());
                string return_info = reader.ReadToEnd();
                reader.Close();
                response.Close();
                //Compare current production version to current installed version
                double prod_version = Convert.ToDouble(return_info);
                if (prod_version > version) return true;
                else return false;
            }
            catch(Exception ex)
            {
                //An error occurred assume no update available
                return false;
            }
        }

        //log the current user into the campus network
        private int login_user()
        {
            try
            {
                //Ignore invalid certificates caused by proxy redirection
                ServicePointManager.ServerCertificateValidationCallback =
                new RemoteCertificateValidationCallback(
                    delegate
                    { return true; }
                );

                //Login user by posting user data to correct proxy site   
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://proxy.bju.edu:4433/seq/?bcfru=aHR0cDovL2dvb2dsZS5jb20v");
                request.Method = "POST";
                request.AllowAutoRedirect = true;
                
                byte[] post_bytes = Encoding.ASCII.GetBytes("PROXY_SG_USERNAME=" + global_username + "&PROXY_SG_PASSWORD=" + global_password + "&PROXY_SG_REQUEST_ID=&PROXY_SG_PRIVATE_CHALLENGE_STATE=&submit=Log+in");
                Stream writer = request.GetRequestStream();
                writer.Write(post_bytes, 0, post_bytes.Length);
                writer.Close();

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                StreamReader reader = new StreamReader(response.GetResponseStream());
                string return_information = reader.ReadToEnd();
                response.Close();
                reader.Close();
                //Ensure that response was valid and username and password were not incorrect
                //Handle invalid password
                if (return_information.Contains("General authentication failure due to bad user ID or authentication token.") || 
                    return_information.Contains("Credentials are missing."))
                {
                    return -2;
                }
                //Connect successful
                return 0;
            }
            catch (Exception ex)
            {
                //Could not connect
                return -1;
            }
        }

        //log the current user out
        private bool logout_user()
        {
            try
            {
                //Request logout page, logging user out
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://proxylogout.bju.edu/");
                request.AllowAutoRedirect = true;
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                response.Close();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        //Write the user's information out to a file, then encrypt the file to prevent password stealing
        private void save_user_information()
        {
            try
            {
                StreamWriter writer = new StreamWriter(temp_path);
                writer.WriteLine(username.Text);
                writer.WriteLine(password.Text);
                writer.Close();
                EncryptFile(temp_path, file_path);
                File.Delete(temp_path);
            }
            catch (Exception ex)
            {
                ;
            }
        }

        //Read the user's information from an encrypted file.
        private void load_user_information()
        {
            try
            {
                DecryptFile(file_path, temp_path);
                StreamReader reader = new StreamReader(temp_path);
                username.Text = reader.ReadLine();
                password.Text = reader.ReadLine();
                reader.Close();
                File.Delete(temp_path);
            }
            catch (Exception ex)
            {
                username.Text = "";
                password.Text = "";
            }
        }

        //Thread process wrapper for logout process
        private void LogOut()
        {
            bool ret_val = logout_user();
            MethodInvoker invoker;
            if (ret_val)
            {
                //if logout process succeeded
                invoker = new MethodInvoker(LogOut_Success);
            }
            else
            {
                //if logout process failed
                invoker = new MethodInvoker(LogOut_Failure);
            }
            this.BeginInvoke(invoker);
        }

        //Thread process wrapper for login process
        private void LogIn()
        {
            int ret_val = login_user();
            MethodInvoker invoker;
            switch (ret_val)
            {
                case -1:
                    //if connection lost or nonexistent
                    invoker = new MethodInvoker(LogIn_NoConnectivity);
                    break;
                case -2:
                    //if username/password invalid
                    invoker = new MethodInvoker(LogIn_InvalidID);
                    break;
                default:
                    //if connection attempt successful
                    invoker = new MethodInvoker(LogIn_Successful);
                    break;
            }
            this.BeginInvoke(invoker);
                    
        }

        //Handle invalid username/password
        private void LogIn_InvalidID()
        {
            autologin.Enabled = false;
            keep_alive.Enabled = false;
            error.Text = "Incorrect Username/Password";
            logged_in = false;
            login.Enabled = true;
            login.Text = "Login";
            logoutToolStripMenuItem.Enabled = false;
            loginToolStripMenuItem.Enabled = true;
            trayIcon.BalloonTipTitle = "Incorrect Username/Password";
            trayIcon.BalloonTipText = "Either your username or password is incorrect.  Please correct this and retry your login";
            trayIcon.ShowBalloonTip(5);
            thread_completed = true;
        }

        //Handle nonexistent/closed connection
        private void LogIn_NoConnectivity()
        {
            autologin.Enabled = true;
            keep_alive.Enabled = false;
            login.Enabled = true;
            login.Text = "Login";
            logoutToolStripMenuItem.Enabled = false;
            loginToolStripMenuItem.Enabled = true;
            if(logged_in)
            {
                error.Text = "Connection Lost";
                trayIcon.BalloonTipTitle = "Connectivity Lost";
                trayIcon.BalloonTipText = "LogU/\\/ has detected that you have lost connectivity to the BJU network.  Once you are reconnected to the BJU network, you will be relogged into the proxy system.";
                trayIcon.ShowBalloonTip(5);
            }
            logged_in = false;
            thread_completed = true;
        }

        //Connection successful
        private void LogIn_Successful()
        {
            keep_alive.Enabled = true;
            autologin.Enabled = false;
            login.Enabled = true;
            login.Text = "Logout";
            logoutToolStripMenuItem.Enabled = true;
            loginToolStripMenuItem.Enabled = false;
            if(!logged_in)
            {
                error.Text = "";
                trayIcon.BalloonTipTitle = "Login Successful";
                trayIcon.BalloonTipText = "You have been successfully logged into the BJU network.";
                trayIcon.ShowBalloonTip(5);
            }
            logged_in = true;
            thread_completed = true;
        }

        //Handle successful logout
        private void LogOut_Success()
        {
            error.Text = "";
            keep_alive.Enabled = false;
            logoutToolStripMenuItem.Enabled = false;
            loginToolStripMenuItem.Enabled = true;
            logged_in = false;
            login.Enabled = true;
            login.Text = "Login";
            trayIcon.BalloonTipTitle = "Logout Successful";
            trayIcon.BalloonTipText = "You have been successfully logged out of the BJU network.";
            trayIcon.ShowBalloonTip(5);
            thread_completed = true;
        }

        //Handle unsuccessful logout
        private void LogOut_Failure()
        {
            error.Text = "";
            keep_alive.Enabled = false;
            logoutToolStripMenuItem.Enabled = false;
            loginToolStripMenuItem.Enabled = true;
            logged_in = false;
            login.Enabled = true;
            login.Text = "Login";
            trayIcon.BalloonTipTitle = "Logout Successful";
            trayIcon.BalloonTipText = "You have been successfully logged out of the BJU network.";
            trayIcon.ShowBalloonTip(5);
            thread_completed = true;
        }

        //Launch thread to keep user logged in and prevent logout
        private void keep_alive_Tick(object sender, EventArgs e)
        {
            if (thread_completed)
            {
                global_username = username.Text;
                global_password = password.Text;
                thread_completed = false;
                service_thread = new Thread(new ThreadStart(LogIn));
                service_thread.Start();
            }
        }

        //Launch thread to attempt to automatically log user in
        //Do not alert user if connection is not open or autologin
        //fails
        private void autologin_Tick(object sender, EventArgs e)
        {
            if (thread_completed)
            {
                global_username = username.Text;
                global_password = password.Text;
                thread_completed = false;
                loginToolStripMenuItem.Enabled = false;
                login.Enabled = false;
                service_thread = new Thread(new ThreadStart(LogIn));
                service_thread.Start();
            }
        }

        //Handle login/logout button click
        private void login_Click(object sender, EventArgs e)
        {
            thread_completed = false;
            global_username = username.Text;
            global_password = password.Text;
            if (logged_in)
            {
                logoutToolStripMenuItem.Enabled = false;
                login.Enabled = false;
                Thread thread = new Thread(new ThreadStart(LogOut));
                thread.Start();
            }
            else
            {
                loginToolStripMenuItem.Enabled = false;
                login.Enabled = false;
                Thread thread = new Thread(new ThreadStart(LogIn));
                thread.Start();
            }
        }

        //User requested via context menu that program exit
        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Cleanup();
        }

        private void Cleanup()
        {
            save_user_information();
            autologin.Enabled = false;
            keep_alive.Enabled = false;
            trayIcon.Visible = false;
            this.Enabled = false;
            this.Hide();
            try
            {
                service_thread.Abort();
            }
            catch (Exception ex)
            {
                ; //no service_thread to abort, ignore error
            }
            SystemEvents.PowerModeChanged -= new PowerModeChangedEventHandler(PowerModeChanged);
            Application.ExitThread();
        }

        //User requested via context menu that program perform logout
        private void logoutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            logoutToolStripMenuItem.Enabled = false;
            login.Enabled = false;
            Thread thread = new Thread(new ThreadStart(LogOut));
            thread.Start();
        }

        //User requested via context menu that program perform login
        private void loginToolStripMenuItem_Click(object sender, EventArgs e)
        {
            loginToolStripMenuItem.Enabled = false;
            login.Enabled = false;
            Thread thread = new Thread(new ThreadStart(LogIn));
            thread.Start();
        }

        //Capture exit click and minimize to system tray instead of closing, unless close requested
        //by system.
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.FormOwnerClosing && e.CloseReason != CloseReason.ApplicationExitCall)
            {
                //if user clicks exit button to close window, intercept close and change to minimize unless
                //close call was made by system or click exit in traynotify icon
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
            }
            else
            {
                Cleanup();
            }
        }

        //Restore main window when tray icon clicked
        private void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }
    }
}
