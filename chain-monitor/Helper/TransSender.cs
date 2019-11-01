using log4net;
using Newtonsoft.Json.Linq;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Timers;

namespace ChainMonitor.Helper
{
    class TransSender
    {
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        public static void SendTransTimer(List<TransactionInfo> transList)
        {
            //Timer _timer = new Timer(2000);
            //int _retryCount = 0;

            //_timer.Elapsed += (sender, e) =>
            //{
            //    if (!SendTransInfo(transList))
            //    {
            //        ++_retryCount;
            //        if (_retryCount > 30)
            //        {                        
            //            _timer.Dispose();
            //        }                    
            //    }
            //    else
            //    {                   
            //        _timer.Dispose();
            //    }
            //};

            //_timer.AutoReset = true;
            //_timer.Enabled = true;


            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(retryCount: 50, sleepDurationProvider: aa => TimeSpan.FromSeconds(1), onRetry: (exception, aa, retryCount, Context) =>
                {
                    Logger.Warn($"Recharge transInfo send error:,retry count:{retryCount}, exception:{exception.Message} " + exception.StackTrace);
                });

            policy.Execute(() => SendTransInfo(transList));
        }


        /// <summary>
        /// 发送交易数据
        /// </summary>
        /// <param name="transRspList">交易数据列表</param>
        public static void SendTransInfo(List<TransactionInfo> transList)
        {
            if (transList.Count > 0)
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(transList.GetType());
                MemoryStream meStream = new MemoryStream();
                serializer.WriteObject(meStream, transList);
                byte[] dataBytes = new byte[meStream.Length];
                meStream.Position = 0;
                meStream.Read(dataBytes, 0, (int)meStream.Length);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Config._apiDict["blacat"]);
                req.Method = "POST";
                req.ContentType = "application/json;charset=utf-8";

                byte[] data = dataBytes;
                req.ContentLength = data.Length;
                using (Stream reqStream = req.GetRequestStream())
                {
                    reqStream.Write(data, 0, data.Length);
                    reqStream.Close();
                }

                Logger.Info(" Recharge send transInfo : " + Encoding.UTF8.GetString(data));
                HttpWebResponse resp = (HttpWebResponse)req.GetResponseAsync().Result;
                Stream stream = resp.GetResponseStream();
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var result = reader.ReadToEnd();
                    var rjson = JObject.Parse(result);

                    if (Convert.ToInt32(rjson["r"]) == 1)
                    {
                        //保存交易信息
                        DbHelper.SaveTransInfo(transList);
                    }
                    else
                    {
                        Logger.Warn("Recharge transInfo send fail:" + result);
                        throw new Exception("Recharge transInfo send fail: " + result);
                    }

                }

            }

        }

    }
}
