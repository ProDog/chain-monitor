using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using System.Text;
using log4net;

namespace ChainMonitor.Helper
{
    public class DbHelper
    {
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string dbName = "MonitorData.db";
        public static void CreateDb()
        {
            if (File.Exists(dbName))
                return;
            SQLiteConnection.CreateFile(dbName);
            string sqlString = "CREATE TABLE Transactions (CoinType TEXT NOT NULL,Height INTEGER,Txid TEXT NOT NULL,ToAddress TEXT,Value REAL NOT NULL,ConfirmCount INTEGER NOT NULL,UpdateTime TEXT NOT NULL,PRIMARY KEY (\"CoinType\", \"Txid\"));" +
                               "CREATE TABLE Address (CoinType TEXT NOT NULL,Address TEXT NOT NULL,DateTime TEXT NOT NULL);" +
                               "CREATE TABLE ParseHeight (CoinType TEXT PRIMARY KEY NOT NULL,Height INTEGER NOT NULL,DateTime TEXT NOT NULL);";
            SQLiteConnection conn = new SQLiteConnection();
            conn.ConnectionString = "DataSource = " + dbName;
            conn.Open();           
            SQLiteCommand cmd = new SQLiteCommand(conn)
            {
                CommandText = sqlString
            };
            cmd.ExecuteNonQuery();
            conn.Close();
        }

        /// <summary>
        /// 保存监控地址
        /// </summary>
        /// <param name="json"></param>
        public static void SaveAddress(string coinType, string address)
        {
            var sql =
                $"insert into Address (CoinType,Address,DateTime) values ('{coinType}','{address}','{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}')";
            ExecuteSql(sql);
        }

        /// <summary>
        /// 保存交易信息
        /// </summary>
        /// <param name="transList"></param>
        public static void SaveTransInfo(List<TransactionInfo> transList)
        {
            StringBuilder sbSql = new StringBuilder();
            foreach (var tran in transList)
            {
                sbSql.Append(
                    $"Replace into Transactions (CoinType,Height,Txid,ToAddress,Value,ConfirmCount,UpdateTime) values ('{tran.coinType}',{tran.height},'{tran.txid}','{tran.toAddress}',{tran.value},{tran.confirmCount},'{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}');");
            }
            ExecuteSql(sbSql.ToString());
        }

        public static List<TransactionInfo> GetTransList(ref List<TransactionInfo> transList, int count, string type)
        {
            var sql = $"select CoinType,Height,Txid, ToAddress,Value,ConfirmCount from Transactions where CoinType = '{type}' and ConfirmCount < {count}";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var trans = new TransactionInfo();
                    trans.coinType = table.Rows[i]["CoinType"].ToString();
                    trans.toAddress = table.Rows[i]["ToAddress"].ToString();
                    trans.txid = table.Rows[i]["Txid"].ToString();
                    trans.confirmCount = Convert.ToUInt32(table.Rows[i]["ConfirmCount"]);
                    trans.height = Convert.ToUInt32(table.Rows[i]["Height"]);
                    trans.value = Convert.ToDecimal(table.Rows[i]["Value"]);
                    transList.Add(trans);
                }
            }
            return transList;
        }

        public static List<TransactionInfo> GetETHTransList(ref List<TransactionInfo> transList, int count)
        {
            var sql = $"select CoinType,Height,Txid, ToAddress,Value,ConfirmCount from Transactions where CoinType in ('eth','{Config._coinname}') and ConfirmCount < {count}";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var trans = new TransactionInfo();
                    trans.coinType = table.Rows[i]["CoinType"].ToString();
                    trans.toAddress = table.Rows[i]["ToAddress"].ToString();
                    trans.txid = table.Rows[i]["Txid"].ToString();
                    trans.confirmCount = Convert.ToUInt32(table.Rows[i]["ConfirmCount"]);
                    trans.height = Convert.ToUInt32(table.Rows[i]["Height"]);
                    trans.value = Convert.ToDecimal(table.Rows[i]["Value"]);
                    transList.Add(trans);
                }
            }
            return transList;
        }

        internal static List<string> GetBgaAddr()
        {
            var list = new List<string>();
            var sql = "select Address from Address where CoinType='bga' ";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    list.Add(table.Rows[i][0].ToString());
                }
            }

            return list;
        }

        public static List<string> GetBtcAddr()
        {
            var list = new List<string>();
            var sql = "select Address from Address where CoinType='btc' ";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    list.Add(table.Rows[i][0].ToString());
                }
            }

            return list;
        }

        public static List<string> GetZoroAddr()
        {
            var list = new List<string>();
            var sql = "select Address from Address where CoinType='zoro' ";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    list.Add(table.Rows[i][0].ToString());
                }
            }

            return list;
        }

        public static HashSet<string> GetEthAddr()
        {
            var list = new HashSet<string>();
            var sql = "select Address from Address where CoinType='eth' or CoinType='bga' ";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    if (!list.Contains(table.Rows[i][0].ToString().ToLower()))
                        list.Add(table.Rows[i][0].ToString().ToLower());
                }
            }

            return list;
        }

        public static List<string> GetNeoAddr()
        {
            var list = new List<string>();
            var sql = "select Address from Address where CoinType='neo' or CoinType='gas' ";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0)
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    list.Add(table.Rows[i][0].ToString());
                }
            }

            return list;
        }

        public static void SaveIndex(ulong i, string type)
        {
            var sql = $"Replace into ParseHeight (CoinType,Height,DateTime) values ('{type}',{i},'{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}')";
            ExecuteSql(sql);
        }

        public static ulong GetIndex(string coinType)
        {
            var sql = $"select Height from ParseHeight where CoinType='{coinType}' ";
            var table = ExecuSqlToDataTable(sql);
            if (table.Rows.Count > 0 && !string.IsNullOrEmpty(table.Rows[0][0].ToString()))
                return Convert.ToUInt64(table.Rows[0][0]) + 1;
            return 1;
        }                

        private static void ExecuteSql(string sql)
        {
            SQLiteConnection conn = new SQLiteConnection("Data Source = " + dbName);
            conn.Open();
            //事务操作
            SQLiteTransaction trans = conn.BeginTransaction();
            SQLiteCommand cmd = new SQLiteCommand(conn);
            cmd.Transaction = trans;
            cmd.CommandText = sql.ToString();
            try
            {
                cmd.ExecuteNonQuery();
                trans.Commit();
            }
            catch (Exception ex)
            {
                Logger.Error("db error:" + ex.Message);
                Logger.Error("stack: " + ex.StackTrace);
                trans.Rollback();
            }
            finally
            {
                conn.Close();
            }
        }

        private static DataTable ExecuSqlToDataTable(string sql)
        {
            DataTable table = new DataTable();
            SQLiteConnection conn = new SQLiteConnection("Data Source = " + dbName);
            SQLiteCommand cmd = new SQLiteCommand(sql, conn);
            SQLiteDataAdapter sqliteDa = new SQLiteDataAdapter(cmd);
            conn.Open();
            sqliteDa.Fill(table);
            conn.Close();
            return table;
           
        }

    }
}
