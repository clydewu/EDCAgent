using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;

using EDCLogger;

namespace EDCAgentCore
{
    public class EDCAgent
    {
        #region Const variables
        const string SPSyncEDCInfo = "sp_SyncEDCInfo";
        const string kModuleName = "EDCAgent";
        const string kSyncVerCmd = "SYNC_VER";
        const string kSyncEmpCmd = "SYNC_EMP";
        const string kSyncEDCCmd = "SYNC_EDC";
        const string kSyncProjCmd = "SYNC_PROJ";
        const string kSyncLogCmd = "SYNC_LOG";
        const string kSyncEmpDeltaCmd = "SYNC_EMP_DELTA";
        const string kSyncEDCDeltaCmd = "SYNC_EDC_DELTA";
        const string kSyncProjDeltaCmd = "SYNC_PROJ_DELTA";
        const string kSyncEmpDeltaOkCmd = "SYNC_EMP_DELTA_OK";
        const string kSyncEDCDeltaOkCmd = "SYNC_EDC_DELTA_OK";
        const string kSyncProjDeltaOkCmd = "SYNC_PROJ_DELTA_OK";
        const string kSyncLogOKCmd = "SYNC_LOG_OK";
        const string kSyncLogDupCmd = "SYNC_LOG_DUP";
        const string kFieldEDCID = "@edc_id";
        const string kFieldEDCVER = "@edc_ver";
        const string kFieldEDCLog = "@edc_log";
        const string kGrayBig = "GB";
        const string kGraySmall = "GS";
        const string kGrayOther = "GO";
        const string kColorBig = "CB";
        const string kColorSmall = "CS";
        const string kColorOther = "CO";
        const int kSize = 256;
        const int kDefPort = 3000;
        const int kBufSize = 256;
        #endregion

        private TcpListener tcpListener;
        private Thread listenThread;
        private List<TcpClient> connectedClient;
        private int port;
        private string sqlConnStr;
        private EDCLogger.EDCLogger logger;

        //private Form1 serverfrm;
        public EDCAgent(EDCLogger.EDCLogger edcLogger, int servicePort, string connString)
        {
            this.connectedClient = new List<TcpClient>();
            this.sqlConnStr = connString;
            this.port = servicePort;
            this.logger = edcLogger;
        }

        public void Start()
        {
            try
            {
                this.tcpListener = new TcpListener(IPAddress.Any, port);
                this.listenThread = new Thread(new ThreadStart(thrListenForClients));
                this.listenThread.Start();
                logger.Log(LogLevel.INFO, "Start listening");
            }
            catch (Exception ex)
            {
                logger.Log(LogLevel.ERROR, String.Format("Error occur when start listening: {0}", ex.Message));
            }
        }

        public void Close()
        {
            foreach (TcpClient client in connectedClient)
            {
                logger.Log(LogLevel.INFO, string.Format("Close connection, ip: {0}", client.Client.RemoteEndPoint.ToString()));
                client.Close();
            }

            if (tcpListener.Server.IsBound)
            {
                logger.Log(LogLevel.INFO, string.Format("Stop listening"));
                tcpListener.Stop();
            }

            if (listenThread != null && listenThread.ThreadState == System.Threading.ThreadState.Running)
            {
                logger.Log(LogLevel.INFO, string.Format("Stop listen thread"));
                this.listenThread.Abort();
            }
        }

        private void thrListenForClients()
        {
            if (!tcpListener.Server.IsBound)
            {
                this.tcpListener.Start();
            }

            while (true)
            {
                try
                {
                    // Blocking here until a client has connected to the server
                    TcpClient client = this.tcpListener.AcceptTcpClient();
                    connectedClient.Add(client);
                    logger.Log(LogLevel.INFO, "New Client connected: " + client.Client.RemoteEndPoint.ToString());
                    //create a thread to handle communication with connected client
                    Thread clientThread = new Thread(new ParameterizedThreadStart(thrHandleClientComm));
                    clientThread.Start(client);
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.INFO, string.Format("Error occur when establish client thread, Exception: {0}", ex.Message));
                }
            }
        }

        private void thrHandleClientComm(object client)
        {
            TcpClient tcpClient = (TcpClient)client;
            string clientIP = tcpClient.Client.RemoteEndPoint.ToString();
            NetworkStream clientStream;
            byte[] buf_read;
            byte[] send_buf;
            int bytes_read;
            string recv_str = "";
            List<string> command_list = new List<string>();

            try
            {
                clientStream = tcpClient.GetStream();
                logger.LogText(LogLevel.INFO, string.Format("Client thread start: {0}", clientIP));
                while (true)
                {
                    bytes_read = 0;
                    try
                    {
                        buf_read = new byte[kBufSize];
                        bytes_read = clientStream.Read(buf_read, 0, kBufSize);
                    }
                    catch (Exception ex)
                    {
                        logger.FatalText(string.Format("Client stream IO error when reading, ip {0}, expexction: {1}", clientIP, ex));
                        break;
                    }

                    if (bytes_read == 0)
                    {
                        logger.InfoText(string.Format("EDCClient disconnected, ip: {0}", clientIP));
                        break;
                    }

                    //message has successfully been received
                    ASCIIEncoding encoder = new ASCIIEncoding();
                    recv_str += encoder.GetString(buf_read, 0, bytes_read);
                    while (true)
                    {
                        // header include '|'
                        int header_len = recv_str.IndexOf("|") + 1;
                        int content_length = 0;
                        if (header_len <= 1)
                        {
                            //Can't find '|', read more
                            break;
                        }

                        try
                        {
                            content_length = int.Parse(recv_str.Substring(0, header_len - 1));
                        }
                        catch
                        {
                            logger.InfoText(string.Format("Protocol error, skip current received payload, ip: {0}", clientIP));
                            recv_str = "";
                            break;
                        }

                        if (recv_str.Length - header_len < content_length)
                        {
                            logger.DebugText(string.Format("There are still unread payload, read again, ip: {0}", clientIP));
                            break;
                        }

                        // OK, now I have at least one command
                        string command = recv_str.Substring(header_len, content_length);
                        //By now, if two command arrived at the same time, only first will be handled
                        //But I don't know whether it is possible.
                        string[] cmd_tokens = command.Split('\n')[0].Split('\t');
                        string cmd = cmd_tokens[0];
                        string send_str = "";
                        switch (cmd)
                        {
                            case kSyncVerCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) sync version: {2}", cmd_tokens[2], clientIP, cmd_tokens[1]));
                                updateEDCVersion(cmd_tokens);
                                logger.InfoText(string.Format("Sync version: {1}", clientIP, cmd_tokens[1]));
                                break;
                            case kSyncEmpCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) require to sync Employee List", cmd_tokens[2], clientIP));
                                send_str = getEmployeeList(cmd_tokens);
                                send_buf = encoder.GetBytes(send_str);
                                clientStream.Write(send_buf, 0, send_buf.Length);
                                clientStream.Flush();
                                logger.DebugText(string.Format("Send Employee list to Client EDC{0}({1}) OK", cmd_tokens[2], clientIP));
                                break;
                            case kSyncEDCCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) require to sync EDC List", cmd_tokens[2], clientIP));
                                send_str = getEDCList(cmd_tokens);
                                send_buf = encoder.GetBytes(send_str);
                                clientStream.Write(send_buf, 0, send_buf.Length);
                                clientStream.Flush();
                                logger.DebugText(string.Format("Send EDC list to Client EDC{0}({1}) OK", cmd_tokens[2], clientIP));
                                break;
                            case kSyncProjCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) require to sync Project List", cmd_tokens[2], clientIP));
                                send_str = getProjectList(cmd_tokens);
                                send_buf = encoder.GetBytes(send_str);
                                clientStream.Write(send_buf, 0, send_buf.Length);
                                clientStream.Flush();
                                logger.DebugText(string.Format("Send Project list to Client EDC{0}({1}) OK", cmd_tokens[2], clientIP));
                                break;
                            case kSyncLogCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) sync EDC Log", cmd_tokens[2], clientIP));
                                syncEDCLog(command, clientStream);
                                logger.DebugText(string.Format("Sync EDCLog from Client EDC{0}({1}) OK", cmd_tokens[2], clientIP));
                                break;
                            case kSyncEmpDeltaCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) require to sync Employee Delta", cmd_tokens[2], clientIP));
                                send_str = getEmployeeDelta(cmd_tokens);
                                send_buf = encoder.GetBytes(send_str);
                                //TODO write error check
                                clientStream.Write(send_buf, 0, send_buf.Length);
                                clientStream.Flush();
                                logger.DebugText(string.Format("Send Employee delta to Client EDC{0}({1}) OK", cmd_tokens[2], clientIP));
                                break;
                            case kSyncEDCDeltaCmd:
                                logger.WarnText(string.Format("Client EDC{0}({1}) require to sync EDC Delta, but this isn't implement", cmd_tokens[2], clientIP));
                                break;
                            case kSyncProjDeltaCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) require to sync Project Delta", cmd_tokens[2], clientIP));
                                send_str = getProjectDelta(cmd_tokens);
                                send_buf = encoder.GetBytes(send_str);
                                clientStream.Write(send_buf, 0, send_buf.Length);
                                clientStream.Flush();
                                logger.DebugText(string.Format("Send Project delta to Client EDC{0}({1}) OK", cmd_tokens[2], clientIP));
                                break;
                            case kSyncEmpDeltaOkCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) response sync Employee Delta OK", cmd_tokens[2], clientIP));
                                handleEmployeeDeltaOk(cmd_tokens);
                                break;
                            case kSyncEDCDeltaOkCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) response sync EDC Delta OK", cmd_tokens[2], clientIP));
                                handleEDCDeltaOk(cmd_tokens);
                                break;
                            case kSyncProjDeltaOkCmd:
                                logger.InfoText(string.Format("Client EDC{0}({1}) response sync Project Delta OK", cmd_tokens[2], clientIP));
                                handleProjectDeltaOk(cmd_tokens);
                                break;
                            default:
                                break;
                        }
                        int protocol_len = header_len + content_length;

                        if (recv_str.Length > protocol_len)
                        {
                            recv_str = recv_str.Substring(header_len + content_length);
                        }
                        else
                        {
                            recv_str = "";
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(string.Format("Unexception error occur in client, ip: {0}, exception: {1}", clientIP, ex.Message));
            }
            finally
            {
                tcpClient.Close();
            }

            connectedClient.Remove(tcpClient);
            logger.Error(string.Format("Client thread terminated, ip: {0}", clientIP));
        }

        private string getEmployeeDelta(string[] plist)
        {
            StringBuilder emp_list = new StringBuilder();
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_cmd = new SqlCommand(SPSyncEDCInfo, sql_conn))
                {
                    sql_cmd.CommandTimeout = 0;
                    sql_cmd.CommandType = CommandType.StoredProcedure;
                    sql_cmd.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = "get_sync_emp";
                    sql_cmd.Parameters.Add("@EDCNO", SqlDbType.VarChar, 50).Value = plist[1];
                    using (SqlDataReader sql_reader = sql_cmd.ExecuteReader())
                    {
                        while (sql_reader.Read())
                        {
                            emp_list.Append(sql_reader["DepartmentName"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["DepartmentNo"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["UserNumber"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["CardNumber"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["IniQuota"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["NowQuota"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["IsColorPrint"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["StatusType"]);
                            emp_list.Append("\n");
                        }
                        emp_list.Insert(0, emp_list.Length.ToString() + "|");
                    }
                }
            }
            return emp_list.ToString();
        }

        private string getProjectDelta(string[] plist)
        {
            StringBuilder proj_list = new StringBuilder();
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_cmd = new SqlCommand(SPSyncEDCInfo, sql_conn))
                {
                    sql_cmd.CommandTimeout = 0;
                    sql_cmd.CommandType = CommandType.StoredProcedure;
                    sql_cmd.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = "get_sync_prj";
                    sql_cmd.Parameters.Add("@EDCNO", SqlDbType.VarChar, 50).Value = plist[1];
                    using (SqlDataReader sql_reader = sql_cmd.ExecuteReader())
                    {
                        while (sql_reader.Read())
                        {
                            proj_list.Append(sql_reader["ProjectNO"]);
                            proj_list.Append("\t");
                            proj_list.Append(sql_reader["StatusType"]);
                            proj_list.Append("\n");
                        }
                        proj_list.Insert(0, proj_list.Length.ToString() + "|");
                    }
                }
            }
            return proj_list.ToString();
        }

        private void handleEmployeeDeltaOk(string[] plist)
        {
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_cmd = new SqlCommand(SPSyncEDCInfo, sql_conn))
                {
                    sql_cmd.CommandTimeout = 0;
                    sql_cmd.CommandType = CommandType.StoredProcedure;
                    sql_cmd.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = "del_sync_emp";
                    sql_cmd.Parameters.Add("@EDCNO", SqlDbType.VarChar, 50).Value = plist[1];
                    sql_cmd.ExecuteNonQuery();
                }
            }
        }

        private void handleEDCDeltaOk(string[] plist)
        {
            return;
            /*
            SqlCommand sql_cmd;
            *
            sql_cmd = new SqlCommand("sp_SyncEDCInfo", sqlConn);
            sql_cmd.CommandType = CommandType.StoredProcedure;
            sql_cmd.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = "del_sync_edc";
            sql_cmd.Parameters.Add("@EDCNO", SqlDbType.VarChar, 50).Value = plist[1];
            sql_cmd.ExecuteNonQuery();
            */
        }

        private void handleProjectDeltaOk(string[] plist)
        {
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_cmd = new SqlCommand(SPSyncEDCInfo, sql_conn))
                {
                    sql_cmd.CommandTimeout = 0;
                    sql_cmd.CommandType = CommandType.StoredProcedure;
                    sql_cmd.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = "del_sync_prj";
                    sql_cmd.Parameters.Add("@EDCNO", SqlDbType.VarChar, 50).Value = plist[1];
                    sql_cmd.ExecuteNonQuery();
                }
            }
        }

        private string getEmployeeList(string[] plist)
        {
            StringBuilder emp_list = new StringBuilder();
            string sql_select = "SELECT " +
            "[dbo].[DataDepartment].[DepartmentName]," +
            "[dbo].[DataEmployee].[DepartmentNo]," +
            "[dbo].[DataEmployee].[UserNumber]," +
            "[dbo].[DataEmployee].[CardNumber]," +
            "[dbo].[DataEmployee].[IniQuota]," +
            "[dbo].[DataEmployee].[NowQuota]," +
            "[dbo].[DataEmployee].[IsColorPrint] " +
            "FROM [dbo].[DataEmployee] JOIN [dbo].[DataDepartment]" +
            "ON [dbo].[DataEmployee].[DepartmentNo] = [dbo].[DataDepartment].[DepartmentNo]";
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_cmd = new SqlCommand(sql_select, sql_conn))
                {
                    sql_cmd.CommandTimeout = 0;
                    sql_cmd.CommandType = System.Data.CommandType.Text;
                    using (SqlDataReader sql_reader = sql_cmd.ExecuteReader())
                    {
                        while (sql_reader.Read())
                        {
                            emp_list.Append(sql_reader["DepartmentName"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["DepartmentNo"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["UserNumber"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["CardNumber"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["IniQuota"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["NowQuota"]);
                            emp_list.Append("\t");
                            emp_list.Append(sql_reader["IsColorPrint"]);
                            emp_list.Append("\n");
                        }
                        emp_list.Insert(0, emp_list.Length.ToString() + "|");
                    }
                }
            }
            return emp_list.ToString();
        }

        private string getProjectList(string[] plist)
        {
            StringBuilder proj_list = new StringBuilder();
            string sql_str = "SELECT [dbo].[DataProject].[ProjectNO] FROM [dbo].[DataProject]";
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_cmd = new SqlCommand(sql_str, sql_conn))
                {
                    sql_cmd.CommandTimeout = 0;
                    sql_cmd.CommandType = System.Data.CommandType.Text;
                    using (SqlDataReader sql_reader = sql_cmd.ExecuteReader())
                    {
                        while (sql_reader.Read())
                        {
                            proj_list.Append(sql_reader["ProjectNO"]);
                            proj_list.Append("\n");
                        }
                        proj_list.Insert(0, proj_list.Length.ToString() + "|");
                    }
                }
            }
            return proj_list.ToString();
        }

        private string getEDCList(string[] plist)
        {
            StringBuilder edc_list = new StringBuilder();
            //SqlDataReader sql_reader;
            DataSet edc_dataset = new DataSet();
            DataSet pp_dataset = new DataSet();
            string edc_id = plist[1];
            string gray_big = "";
            string gray_small = "";
            string gray_other = "";
            string color_big = "";
            string color_small = "";
            string color_other = "";
            int[] paper_size = new int[] { 4, 5 }; //NOTE default value is here!!!
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                string sql_select = string.Format("SELECT * FROM [dbo].[DataEDC] WHERE [dbo].[DataEDC].[EDCNO] = '{0}'", edc_id);
                using (SqlCommand sql_cmd = new SqlCommand(sql_select, sql_conn))
                {
                    sql_cmd.CommandType = System.Data.CommandType.Text;
                    using (SqlDataAdapter sql_adapter = new SqlDataAdapter(sql_cmd))
                    {
                        sql_cmd.CommandTimeout = 0;
                        sql_adapter.Fill(edc_dataset, "EDC");
                    }
                }

                if (edc_dataset.Tables[0].Rows.Count == 0)
                {
                    logger.InfoText(string.Format("No information of EDC{0} in DB", edc_id));
                }
                else
                {
                    string sql_heartbeat = string.Format("UPDATE [dbo].[DataEDC] SET [dbo].[DataEDC].[EDCDT] = getdate() WHERE [dbo].[DataEDC].[EDCNO] = '{0}'", edc_id);
                    using (SqlCommand sql_cmd = new SqlCommand(sql_heartbeat, sql_conn))
                    {
                        sql_cmd.CommandType = System.Data.CommandType.Text;
                        if (sql_cmd.ExecuteNonQuery() != 1)
                        {
                            logger.ErrorText(string.Format("Write EDC{0} heartbeat to DB error", edc_id));
                        }
                    }
                    string sql_printpay = string.Format("SELECT * FROM [dbo].[DataPrintPay]");
                    using (SqlCommand sql_cmd = new SqlCommand(sql_printpay, sql_conn))
                    {
                        sql_cmd.CommandType = System.Data.CommandType.Text;
                        using (SqlDataAdapter sql_adapter = new SqlDataAdapter(sql_cmd))
                        {
                            sql_adapter.Fill(pp_dataset, "PrintPay");
                            edc_list.Append(edc_dataset.Tables["EDC"].Rows[0]["EDCNO"]);
                            edc_list.Append("\t");
                            edc_list.Append(edc_dataset.Tables["EDC"].Rows[0]["EDCIP"]);
                            edc_list.Append("\t");
                            edc_list.Append(edc_dataset.Tables["EDC"].Rows[0]["MachineIP"]);
                            edc_list.Append("\t");
                            edc_list.Append(edc_dataset.Tables["EDC"].Rows[0]["EDCLimitTime"]);
                            edc_list.Append("\t");
                            edc_list.Append(edc_dataset.Tables["EDC"].Rows[0]["EDCShowLimitTime"]);
                            edc_list.Append("\t");
                            foreach (DataRow pp_row in pp_dataset.Tables["Printpay"].Rows)
                            {
                                if (pp_row["PrintType"].ToString() == kGrayBig)
                                {
                                    gray_big = pp_row["PrintPay"].ToString();
                                }
                                else if (pp_row["PrintType"].ToString() == kGraySmall)
                                {
                                    gray_small = pp_row["PrintPay"].ToString();
                                    // !NOTE! only use gray_small setup to determine whole setup
                                    paper_size = getSmallerPaperSize(pp_row["PaperType"].ToString());
                                }
                                else if (pp_row["PrintType"].ToString() == kGrayOther)
                                {
                                    gray_other = pp_row["PrintPay"].ToString();
                                }
                                else if (pp_row["PrintType"].ToString() == kColorBig)
                                {
                                    color_big = pp_row["PrintPay"].ToString();
                                }
                                else if (pp_row["PrintType"].ToString() == kColorSmall)
                                {
                                    color_small = pp_row["PrintPay"].ToString();
                                }
                                else if (pp_row["PrintType"].ToString() == kColorOther)
                                {
                                    color_other = pp_row["PrintPay"].ToString();
                                }
                            }
                            edc_list.Append(gray_big);
                            edc_list.Append("\t");
                            edc_list.Append(gray_small);
                            edc_list.Append("\t");
                            edc_list.Append(color_big);
                            edc_list.Append("\t");
                            edc_list.Append(color_small);
                            edc_list.Append("\t");
                            edc_list.Append(paper_size[0].ToString());
                            edc_list.Append("\t");
                            edc_list.Append(paper_size[1].ToString());
                            edc_list.Append("\n");
                        }
                    }
                }
            }
            edc_list.Insert(0, edc_list.Length.ToString() + "|");
            return edc_list.ToString();
        }

        private void updateEDCVersion(string[] plist)
        {
            if (plist.Length < 3)
            {
                throw new Exception(string.Format("EDC version command is malform: '{0}'", string.Join(" ", plist)));
            }
            string edc_version = plist[1];
            string edc_no = plist[2];
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                using (SqlCommand sql_update = new SqlCommand("UPDATE [dbo].[DataEDC] SET [EDCVer] = @edc_ver WHERE [EDCNO] = @edc_no", sql_conn))
                {
                    sql_update.CommandTimeout = 0;
                    sql_update.Parameters.AddWithValue("@edc_ver", edc_version);
                    sql_update.Parameters.AddWithValue("@edc_no", edc_no);
                    sql_update.CommandType = System.Data.CommandType.Text;
                    sql_update.ExecuteNonQuery();
                }
            }
        }

        private int[] getSmallerPaperSize(string paper_type)
        {
            string[] tokens = paper_type.Split(';');
            int[] smallest_paper_size = new int[2];
            smallest_paper_size[0] = 4;
            smallest_paper_size[1] = 5;
            foreach (string token in tokens)
            {
                string psize = token.ToLower();
                int val;
                try
                {
                    val = int.Parse(psize.Substring(1));
                }
                catch
                {
                    // If format error, skip it
                    continue;
                }

                if (psize.StartsWith("a"))
                {
                    if (val < smallest_paper_size[0])
                    {
                        smallest_paper_size[0] = val;
                    }
                }
                else if (psize.StartsWith("b"))
                {
                    if (val < smallest_paper_size[1])
                    {
                        smallest_paper_size[1] = val;
                    }
                }
            }
            return smallest_paper_size;
        }

        private void syncEDCLog(string recv, NetworkStream clientStream)
        {
            string[] recv_list;
            recv_list = recv.Split('\n');
            ASCIIEncoding encoder = new ASCIIEncoding();
            string send_str;
            byte[] send_buf;
            DataSet edc_archive = new DataSet();
            using (SqlConnection sql_conn = new SqlConnection(this.sqlConnStr))
            {
                sql_conn.Open();
                //Start from 2nd line
                for (int i = 1; i < recv_list.Length; i++)
                {
                    if (recv_list[i].Trim().Length != 0)
                    {
                        EDCLog edc_log = parseEDCLog(recv_list[i].Trim());
                        //TODO, please note here should change to EDCLogArchive
                        using (SqlCommand sql_insert_log = new SqlCommand("INSERT INTO [dbo].[EDCLogTmp] (EDCLog) VALUES (@edc_log)", sql_conn))
                        {
                            sql_insert_log.CommandTimeout = 0;
                            sql_insert_log.Parameters.Add(kFieldEDCLog, SqlDbType.NVarChar);
                            sql_insert_log.Parameters[kFieldEDCLog].Value = recv_list[i].Trim();
                            sql_insert_log.CommandType = System.Data.CommandType.Text;
                            if (sql_insert_log.ExecuteNonQuery() != 1)
                            {
                                logger.ErrorText("Insert EDCLOG to EDCLogTmp failure");
                            }
                            else
                            {
                                logger.InfoText("Client thread insert EDC_log: " + recv_list[i]);
                            }
                        }

                        if (edc_log.type == "CARD")
                        {
                            string[] content_token = edc_log.content.Split(' ');
                            if (content_token[0] == "VALID")
                            {
                                //TODO Exception here!!!其他資訊: 索引在陣列的界限之外。
                                //getdate()改用edc_log.log_time寫入EDC的時間
                                string sql_insert_pq = string.Format("INSERT INTO [dbo].[PQCardInfo] (EDCNO, CardNumber, UserNumber, ProjectNO, CardDT)" +
                                "VALUES ('{0}', '{1}', '{2}', '{3}', '{4}')", edc_log.edc_no, content_token[1], edc_log.emp_no, edc_log.project_no, edc_log.log_time);
                                using (SqlCommand cmd_Insert_pq = new SqlCommand(sql_insert_pq, sql_conn))
                                {
                                    cmd_Insert_pq.CommandType = System.Data.CommandType.Text;
                                    if (cmd_Insert_pq.ExecuteNonQuery() != 1)
                                    {
                                        logger.ErrorText(string.Format("Insert PQCardInfo to DB error: sql: {0}", sql_insert_pq));
                                    }
                                    else
                                    {
                                        logger.DebugText(string.Format("Insert PQCardInfo to DB success, sql: {0}", sql_insert_pq));
                                    }
                                }
                            }
                        }
                        else if (edc_log.type == "PRINT" || edc_log.type == "COPY")
                        {
                            List<KeyValuePair<string, int>> paper_usage = parseCountContent(edc_log.content);
                            foreach (KeyValuePair<string, int> usage in paper_usage)
                            {
                                string sql_insert_cc = string.Format("INSERT INTO [dbo].[CopyCount] (EDCNO, ProjectNO, UserNumber, PrintType, PrintCount, UseDT)" +
                                "VALUES ('{0}', '{1}', '{2}', '{3}', '{4}', '{5}')",
                                edc_log.edc_no, edc_log.project_no, edc_log.emp_no, usage.Key, usage.Value, edc_log.log_time);
                                using (SqlCommand cmd_insert_cc = new SqlCommand(sql_insert_cc, sql_conn))
                                {
                                    cmd_insert_cc.CommandType = System.Data.CommandType.Text;
                                    if (cmd_insert_cc.ExecuteNonQuery() != 1)
                                    {
                                        logger.ErrorText(string.Format("Insert CopyCount to DB error: sql: {0}", sql_insert_cc));
                                    }
                                    else
                                    {
                                        logger.DebugText(string.Format("Insert CopyCount to DB success: sql: {0}", sql_insert_cc));
                                    }
                                }
                            }
                        }
                        else if (edc_log.type == "SCAN")
                        {
                            string sql_scan = string.Format("INSERT INTO [dbo].[SEFScanInfo] (EDCNO, UserNumber, ProjectNo, ScanDT)" +
                            "VALUES ( '{0}', '{1}', '{2}', '{3}')", edc_log.edc_no, edc_log.emp_no, edc_log.project_no, edc_log.log_time);
                            using (SqlCommand cmd_scan = new SqlCommand(sql_scan, sql_conn))
                            {
                                cmd_scan.CommandType = System.Data.CommandType.Text;
                                if (cmd_scan.ExecuteNonQuery() != 1)
                                {
                                    logger.ErrorText(string.Format("Insert SEFScanInfo to DB error: sql: {0}", sql_scan));
                                }
                                else
                                {
                                    logger.DebugText(string.Format("Insert CopyCount to DB success: sql: {0}", sql_scan));
                                }
                            }
                        }
                        /* 20140908, TODO: Comment for SEQ of EDCLog
                        //Send sync OK to client, EDCClient will drop log until this SYNC_LOG_OK
                        send_str = kSyncLogOKCmd;
                        send_str = send_str.Length.ToString() + "|" + send_str;
                        send_buf = encoder.GetBytes(send_str);
                        clientStream.Write(send_buf, 0, send_buf.Length);
                        clientStream.Flush();
                         */
                    }
                }
            }
            //return true;
        }

        private EDCLog parseEDCLog(string log)
        {
            EDCLog edc_log = new EDCLog();
            string[] token = log.Split('\t');
            // 20140908, TODO: Comment for SEQ of EDCLog
            //List<string> time_token = new List<string>(token[4].Split(':'));
            edc_log.type = token[0];
            edc_log.edc_no = token[1];
            edc_log.project_no = token[2];
            edc_log.emp_no = token[3];
            edc_log.log_time = token[4];
            /* 20140908, Comment for SEQ of EDCLog
            edc_log.log_time_ms = time_token[time_token.Count - 1];
            time_token.RemoveAt(time_token.Count - 1);
            edc_log.log_time_YmdHMS = string.Join(":", time_token);
            */
            edc_log.content = (token.Length > 5) ? token[5] : "";
            return edc_log;
        }

        private List<KeyValuePair<string, int>> parseCountContent(string content)
        {
            List<KeyValuePair<string, int>> usage_list = new List<KeyValuePair<string, int>>();
            string[] paper_usages = content.Split(' ');
            foreach (string paper_usage in paper_usages)
            {
                string[] token = paper_usage.Split(':');
                usage_list.Add(new KeyValuePair<string, int>(token[0], Int16.Parse(token[1])));
            }
            return usage_list;
        }
    }
}
