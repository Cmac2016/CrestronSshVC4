using Crestron.SimplSharp;
using FxSsh;
using FxSsh.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IPAddress = System.Net.IPAddress;

namespace CrestronSshVC4
{
    internal static class SshConsole
    {
        public static readonly List<string> ConsoleCommands = new List<string>();
        private static SshServer _server;
        public static void StartSshConsole()
        {
            ErrorLog.Notice("Starting SSH Console Server");
            try
            {
                _server = new SshServer(new StartingInfo(IPAddress.Any, 2202));
                _server.AddHostKey("ssh-rsa", "BwIAAACkAABSU0EyAAQAAAEAAQADKjiW5UyIad8ITutLjcdtejF4wPA1dk1JFHesDMEhU9pGUUs+HPTmSn67ar3UvVj/1t/+YK01FzMtgq4GHKzQHHl2+N+onWK4qbIAMgC6vIcs8u3d38f3NFUfX+lMnngeyxzbYITtDeVVXcLnFd7NgaOcouQyGzYrHBPbyEivswsnqcnF4JpUTln29E1mqt0a49GL8kZtDfNrdRSt/opeexhCuzSjLPuwzTPc6fKgMc6q4MBDBk53vrFY2LtGALrpg3tuydh3RbMLcrVyTNT+7st37goubQ2xWGgkLvo+TZqu3yutxr1oLSaPMSmf9bTACMi5QDicB3CaWNe9eU73MzhXaFLpNpBpLfIuhUaZ3COlMazs7H9LCJMXEL95V6ydnATf7tyO0O+jQp7hgYJdRLR3kNAKT0HU8enE9ZbQEXG88hSCbpf1PvFUytb1QBcotDy6bQ6vTtEAZV+XwnUGwFRexERWuu9XD6eVkYjA4Y3PGtSXbsvhwgH0mTlBOuH4soy8MV4dxGkxM8fIMM0NISTYrPvCeyozSq+NDkekXztFau7zdVEYmhCqIjeMNmRGuiEo8ppJYj4CvR1hc8xScUIw7N4OnLISeAdptm97ADxZqWWFZHno7j7rbNsq5ysdx08OtplghFPx4vNHlS09LwdStumtUel5oIEVMYv+yWBYSPPZBcVY5YFyZFJzd0AOkVtUbEbLuzRs5AtKZG01Ip/8+pZQvJvdbBMLT1BUvHTrccuRbY03SHIaUM3cTUc=");
                _server.AddHostKey("ssh-dss", "BwIAAAAiAABEU1MyAAQAAG+6KQWB+crih2Ivb6CZsMe/7NHLimiTl0ap97KyBoBOs1amqXB8IRwI2h9A10R/v0BHmdyjwe0c0lPsegqDuBUfD2VmsDgrZ/i78t7EJ6Sb6m2lVQfTT0w7FYgVk3J1Deygh7UcbIbDoQ+refeRNM7CjSKtdR+/zIwO3Qub2qH+p6iol2iAlh0LP+cw+XlH0LW5YKPqOXOLgMIiO+48HZjvV67pn5LDubxru3ZQLvjOcDY0pqi5g7AJ3wkLq5dezzDOOun72E42uUHTXOzo+Ct6OZXFP53ZzOfjNw0SiL66353c9igBiRMTGn2gZ+au0jMeIaSsQNjQmWD+Lnri39n0gSCXurDaPkec+uaufGSG9tWgGnBdJhUDqwab8P/Ipvo5lS5p6PlzAQAAACqx1Nid0Ea0YAuYPhg+YolsJ/ce");
                _server.ConnectionAccepted += server_ConnectionAccepted;
                FxSsh.ConsoleData.OnDataReceived += ConsoleData_OnDataReceived;
                _server.Start();
                Task.Delay(-1).Wait();
            }
            catch (Exception exception)
            {
                ErrorLog.Notice($"Error in Starting Server: {exception.Message}");
            }
        }

        public static void StopSshConsole()
        {
            _server.Stop();
        }
        private static void ConsoleData_OnDataReceived(object sender, DataChangedEventsArgs e)
        {
            ReadConsoleData(e.data);
        }
        private static void server_ConnectionAccepted(object sender, Session e)
        {
            ErrorLog.Notice("Accepted a client.");
            e.ServiceRegistered += e_ServiceRegistered;
        }
        private static void e_ServiceRegistered(object sender, SshService e)
        {
            var session = (Session)sender;
            Console.WriteLine("Session {0} requesting {1}.", BitConverter.ToString(session.SessionId).Replace("-", ""), e.GetType().Name);

            switch (e)
            {
                case UserauthService authService:
                    authService.Userauth += service_UserAuth;
                    break;
                case ConnectionService connectionService:
                    connectionService.CommandOpened += service_CommandOpened;
                    break;
            }
        }
        private static void service_CommandOpened(object sender, SessionRequestedArgs e)
        {
            ErrorLog.Notice("Channel {0} runs command: \"{1}\".", e.Channel.ServerChannelId, e.CommandText);

            var allow = true;
        }
        private static void service_UserAuth(object sender, UserauthArgs e)
        {
            switch (e)
            {
                case PKUserauthArgs pKUserauthArgs:
                    ErrorLog.Notice("Client {0} fingerprint: {1}.", pKUserauthArgs.KeyAlgorithm, pKUserauthArgs.Fingerprint);
                    break;
                case PasswordUserauthArgs passwordUserauthArgs:
                    e.Result = passwordUserauthArgs.Password.ToString() == "12345";
                    break;
                default:
  
                    break;
            }
        }



        private static void ReadConsoleData(string cmd)
        {
            switch (cmd)
            {
                case "help":
                {
                    foreach (var command in ConsoleCommands)
                    {
                        ErrorLog.Notice($"{command}\n");
                        FxSsh.ConsoleData.ConsoleReply($"{command}");
                    }
                    break;
                }
                default:
                    FxSsh.ConsoleData.ConsoleReply($"Unknown Command : {cmd}");
                    break;
            }
        }
    }
}
