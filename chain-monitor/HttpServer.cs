using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using ChainMonitor.Helper;
using log4net;
using Neo.VM;
using Nethereum.Web3;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Zoro;

namespace ChainMonitor
{
    public class HttpServer
    {        
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Http 服务接口
        /// </summary>
        public static void Start()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(Config._apiDict["http"]);
            listener.Start();
            Logger.Info("Http Server Start at " + Config._apiDict["http"]);

            IAsyncResult result = listener.BeginGetContext(new AsyncCallback(ListenerCallback), listener);

            result.AsyncWaitHandle.WaitOne();
        }

        public static void ListenerCallback(IAsyncResult result)
        {
            byte[] buffer = new byte[] { };
            RspInfo rspInfo = new RspInfo() { state = false, msg = "Input data error!" };

            HttpListener listener = (HttpListener)result.AsyncState;
            HttpListenerContext requestContext = listener.EndGetContext(result);
            listener.BeginGetContext(new AsyncCallback(ListenerCallback), listener);

            HttpListenerResponse response = requestContext.Response;

            try
            {
                //获取客户端传递的参数
                StreamReader sr = new StreamReader(requestContext.Request.InputStream);
                var reqMethod = requestContext.Request.RawUrl.Replace("/", "");

                var remoteIP = requestContext.Request.RemoteEndPoint.Address.ToString();
                if (!Config.AllowIPs.Contains(remoteIP))
                {
                    Logger.Warn($"Remote IP Address error:{remoteIP}");
                    return;
                }

                var data = sr.ReadToEnd();
                var json = new JObject();
                if (!string.IsNullOrEmpty(data))
                    json = JObject.Parse(data);

                Logger.Info($"Have a request:{reqMethod} post data: {json}");

                rspInfo = GetResponse(reqMethod, json);

                var rsp = JsonConvert.SerializeObject(rspInfo);
                buffer = Encoding.UTF8.GetBytes(rsp);

                Logger.Info(rsp);
            }
            catch (Exception e)
            {
                var rsp = JsonConvert.SerializeObject(new RspInfo() { state = false, msg = e.Message + e.StackTrace });
                buffer = Encoding.UTF8.GetBytes(rsp);
                Logger.Error(rsp);
                Logger.Error("stack: " + e.StackTrace);
            }

            finally
            {
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.ContentType = "application/json";
                response.ContentEncoding = Encoding.UTF8;
                response.ContentLength64 = buffer.Length;
                var output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();
            }

        }   

        private static RspInfo GetResponse(string reqMethod, JObject json)
        {
            RspInfo rspInfo = new RspInfo() { state = false, msg = "Input data error!" };
            switch (reqMethod)
            {               
                case "getBalance":
                    rspInfo = GetBalanceRsp(json);
                    break;                
                case "addAddress":
                    rspInfo = AddAddressRsp(json);
                    break;
                case "getStatus":
                    rspInfo = GetProcStatus();
                    break;
                        
                default:
                    break;
            }

            return rspInfo;
        }

        private static RspInfo GetProcStatus()
        {
            BlockHeight blockHeight = new BlockHeight();

            blockHeight.ethBlockHeight = EthServer.GetEthBlockHeight();
            blockHeight.ethParseHeight = EthServer.GetEthParseHeight();
            blockHeight.zoroBlockHeight = ZoroServer.GetZoroBlockHeight();
            blockHeight.zoroParseHeight = ZoroServer.GetZoroPraseHeight();

            RspInfo rspInfo = new RspInfo() { state = true, msg = blockHeight };

            return rspInfo;
        }

        private static RspInfo AddAddressRsp(JObject json)
        {
            string coinType = json["coinType"].ToString().ToLower();
            string address = json["address"].ToString();

            switch (coinType)
            {
                case "btc":
                    if (Config._btcAddrList.Exists(x => x == address))
                        break;
                    Config._btcAddrList.Add(address);
                    DbHelper.SaveAddress("btc", address);
                    break;
                case "eth":
                case "usdt":
                case Config._coinname:
                    if (Config._ethAddrList.Contains(address.ToLower()))
                        break;
                    Config._ethAddrList.Add(address.ToLower());
                    DbHelper.SaveAddress("eth", address);
                    break;
                case "neo":
                case "gas":
                    if (Config._neoAddrList.Exists(x => x == address))
                        break;
                    Config._neoAddrList.Add(address);
                    DbHelper.SaveAddress("neo", address);
                    break;
                default:
                    return new RspInfo { state = false, msg = "coinType error!" };
            }

            Logger.Info("Add a new " + coinType + " address: " + address);
            return new RspInfo { state = true, msg = new AccountInfo() { coinType = coinType, address = address } };
        }

        private static RspInfo GetBalanceRsp(JObject json)
        {
            string chain = json["chain"].ToString().ToLower();
            string coinType = json["coinType"].ToString().ToLower();
            string address = json["address"].ToString();

            decimal value = 0;

            switch (chain)
            {
                case "eth":
                    value = GetEthBalance(coinType, address);
                    break;
                case "zoro":
                    value = GetZoroBalance(coinType, address);
                    break;
                case "neo":
                    break;
                case "btc":
                    break;
            }

            return new RspInfo { state = true, msg = new CoinInfon() { coinType = coinType, balance = value } };
        }

        private static decimal GetZoroBalance(string coinType, string address)
        {
            string tokenHash = Config._nep5TokenHashDict[coinType];
            UInt160 nep5Hash = UInt160.Parse(tokenHash);
            var addrHash = ZoroHelper.GetPublicKeyHashFromAddress(address);

            ScriptBuilder sb = new ScriptBuilder();

            if (coinType == "bct" || coinType == "bcp" || coinType == "zoro")
                sb.EmitSysCall("Zoro.NativeNEP5.Call", "BalanceOf", nep5Hash, addrHash);
            else
                sb.EmitAppCall(nep5Hash, "balanceOf", addrHash);

            var info = ZoroHelper.InvokeScript(sb.ToArray(), "");

            JObject json = JObject.Parse(info);
            decimal value = 0;

            if (json.ContainsKey("result"))
            {
                JObject json_result = json["result"] as JObject;
                JArray stack = json_result["stack"] as JArray;

                string result = ZoroHelper.GetJsonValue(stack[0] as JObject);
                value = Math.Round(decimal.Parse(result) / (decimal)Math.Pow(10, Config._nep5TokenDecimalDict[coinType]), Config._nep5TokenDecimalDict[coinType]);
            }

            return value;
        }

        private static decimal GetEthBalance(string coinType, string address)
        {
            var web3 = new Web3(Config._apiDict["eth"]);
            if (coinType == "eth")
            {
                //eth get balance...
                var balanceWei = web3.Eth.GetBalance.SendRequestAsync(address).Result;

                return Web3.Convert.FromWei(balanceWei);
            }

            //erc20 get balance
            var handler = web3.Eth.GetContractHandler(Config._erc20TokenHashDict[coinType]);
            var balanceMessage = new BalanceOfFunction() { Owner = address };
            var value = handler.QueryAsync<BalanceOfFunction, BigInteger>(balanceMessage).Result;

            decimal balance = (decimal)value / (decimal)Math.Pow(10, Config._erc20TokenDecimalDict[coinType]);

            return balance;
        }
    }

}
