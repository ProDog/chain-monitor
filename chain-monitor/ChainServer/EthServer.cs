using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using log4net;
using Nethereum.Geth;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Newtonsoft.Json.Linq;
using Nethereum.RPC.Eth.DTOs;
using ChainMonitor.Helper;
using System.Net;
using System.Text;
using Polly;
using System.Numerics;

namespace ChainMonitor
{
    public class EthServer
    {
        private static List<TransactionInfo> ethTransList = new List<TransactionInfo>(); //ETH 交易列表
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);        

        private static BatchGetTransactionReceipt batchGetReceipt = new BatchGetTransactionReceipt(Config._apiDict["eth"]);

        private static ulong ethHeight;

        public static ulong GetEthParseHeight()
        {
            return ethHeight;
        }

        public static ulong GetEthBlockHeight()
        {
            try
            {
                Web3Geth web3 = new Web3Geth(Config._apiDict["eth"]);
                var aa = web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().Result;
                BigInteger height = aa.Value;
                return (ulong)height;
            }
            catch (Exception ex)
            {
                Logger.Error("Eth get block count error: " + ex.Message);
                Logger.Error("stack: " + ex.StackTrace);
                return 0;
            }
        }

        public static void Start()
        {            
            var policy = Policy.Handle<Exception>()
                .WaitAndRetry(retryCount: 100 , sleepDurationProvider: aa => TimeSpan.FromSeconds(1), onRetry: (exception, aa, retryCount, Context) =>
                 {
                     Logger.Warn($"ETH error,retry count:{retryCount}, exception:{exception.Message} " + exception.StackTrace);
                 });

            ethHeight = Program.ethStartHeight;

            DbHelper.GetETHTransList(ref ethTransList, Config._confirmCountDict["eth"]);
            Logger.Info("Eth watcher start! index: " + ethHeight);

            Web3Geth web3 = new Web3Geth(Config._apiDict["eth"]);

            while (true)
            {
                policy.Execute(() =>
                {
                    var aa = web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().Result;
                    BigInteger height = aa.Value;

                    //Logger.Info("eth current height:" + height);

                    while (ethHeight < height)
                    {
                        ParseEthBlock(web3, ethHeight);

                        DbHelper.SaveIndex(ethHeight, "eth");

                        if (ethHeight % 10 == 0)
                            Logger.Info("Parse ETH Height:" + ethHeight);

                        ethHeight++;
                    }

                    Thread.Sleep(1000);
                });
            }
        }

        /// <summary>
        /// 解析ETH区块
        /// </summary>
        /// <param name="web3"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        private static void ParseEthBlock(Web3Geth web3, ulong index)
        {
            BlockWithTransactions block = web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(index)).Result;

            if (block.Transactions.Length > 0 && Config._ethAddrList.Count > 0)
            {
                batchGetReceipt.beginBuildPar();
                for (var i = 0; i < block.Transactions.Length; i++)
                {
                    var tran = block.Transactions[i];
                    var txid = tran.TransactionHash;

                    //如果存在该 txid 了，说明这笔交易已经解析过了
                    if (ethTransList.Exists(x => x.txid == txid))
                        continue;

                    //先處理ETH交易
                    if (tran.To != null && Config._ethAddrList.Contains(tran.To.ToLower()))
                    {
                        decimal v = (decimal)tran.Value.Value;
                        decimal v2 = 1000000000000000000;
                        var value = v / v2;

                        //最少充值金额0.01
                        if (value >= 0.01m)
                        {
                            var ethTrans = new TransactionInfo();
                            ethTrans.coinType = "eth";
                            ethTrans.toAddress = tran.To.ToString();
                            ethTrans.value = value;
                            ethTrans.confirmCount = 1;
                            ethTrans.height = index;
                            ethTrans.txid = txid;

                            ethTransList.Add(ethTrans);
                            Logger.Info(index + " Have an " + ethTrans.coinType + " transaction to:" + ethTrans.toAddress + "; value:" + value + "; txid:" + ethTrans.txid);
                        }
                    }

                    //是我们监控的合约
                    if (tran.To != null && Config._erc20TokenHashDict.Values.Contains(tran.To.ToLower()))
                        batchGetReceipt.pushTxHash(txid);
                }
                batchGetReceipt.endBuildPar();

                //处理erc20交易
                Erc20TransProc(web3, index);
            }

            //Logger.Info("eth parse height:" + index + " Tx:" + block.Transactions.Length);

            if (ethTransList.Count > 0)
            {
                //更新确认次数
                CheckEthConfirm(ethTransList, index, web3);

                //发送和保存交易信息
                TransSender.SendTransTimer(ethTransList);

                //移除确认次数为 设定数量 和 0 的交易
                ethTransList.RemoveAll(x => x.confirmCount >= Config._confirmCountDict["eth"] || x.confirmCount == 0);
            }
        }

        private static void Erc20TransProc(Web3Geth web3, ulong index)
        {
            //開始處理erc20交易 
            JArray receipts = batchGetReceipt.doRequest() as JArray;

            if (receipts == null)
                return;

            foreach (var jobj in receipts)
            {
                if (jobj["result"] == null || jobj["result"]["transactionHash"] == null)
                {
                    continue;
                }

                if (jobj["result"]["to"] == null || !Config._erc20TokenHashDict.Values.Contains(jobj["result"]["to"].ToString()))
                {
                    continue;
                }

                var txid = jobj["result"]["transactionHash"].ToString();

                //获取转账Receipt 解析所有erc20交易
                TransactionReceipt receipt = web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txid).Result;

                List<Erc20TransferLog> erc20TransferLogList = new List<Erc20TransferLog>();
                foreach (var log in receipt.Logs)
                {
                    string contract = log["address"].ToString();
                    foreach (var token in Config._erc20TokenHashDict)
                    {
                        if (token.Value == contract)
                        {
                            Erc20TransferLog erc20TransferLog = new Erc20TransferLog();

                            JArray topics = log["topics"] as JArray;

                            if (topics.Count >= 3)
                            {
                                erc20TransferLog.toAddress = "0x" + topics[2].ToString().Substring(26);

                                //是否监控地址交易
                                if (!Config._ethAddrList.Contains(erc20TransferLog.toAddress.ToLower()))
                                {
                                    break;
                                }

                                erc20TransferLog.value = log["data"].ToString().HexToBigInteger(false);
                                erc20TransferLog.coinType = token.Key;

                                //usdt最少充值金额为2
                                if (token.Key.ToLower() == "usdt")
                                {
                                    if (erc20TransferLog.value >= 2)
                                        erc20TransferLogList.Add(erc20TransferLog);
                                }
                                else if (erc20TransferLog.value > 0)
                                    erc20TransferLogList.Add(erc20TransferLog);
                            }

                            break;
                        }
                    }
                }

                foreach (Erc20TransferLog erc20TransferLog in erc20TransferLogList)
                {
                    var ethTrans = new TransactionInfo();
                    ethTrans.coinType = erc20TransferLog.coinType;
                    ethTrans.toAddress = erc20TransferLog.toAddress;
                    ethTrans.value = (decimal)erc20TransferLog.value / (decimal)Math.Pow(10, Config._erc20TokenDecimalDict[ethTrans.coinType]);
                    ethTrans.confirmCount = 1;
                    ethTrans.height = index;
                    ethTrans.txid = txid;

                    ethTransList.Add(ethTrans);
                    Logger.Info(index + " Have an " + ethTrans.coinType + " transaction to:" + ethTrans.toAddress + "; value:" + ethTrans.value + "; txid:" + ethTrans.txid);
                }

            }
        }

        /// <summary>
        /// 检查 ETH 确认次数
        /// </summary>
        /// <param name="num"></param>
        /// <param name="ethTransRspList"></param>
        /// <param name="index"></param>
        /// <param name="web3"></param>
        /// <returns></returns>
        private static void CheckEthConfirm(List<TransactionInfo> ethTransRspList, ulong index, Web3Geth web3)
        {
            foreach (var ethTran in ethTransRspList)
            {
                if (index > ethTran.height)
                {
                    BlockWithTransactionHashes hashes = web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(new HexBigInteger(ethTran.height)).Result;

                    //如果原区块中还包含该交易，则确认数 = 当前区块高度 - 交易所在区块高度 + 1，不包含该交易，确认数统一记为 0
                    if (hashes.TransactionHashes.Length > 0 && hashes.TransactionHashes.Contains(ethTran.txid))
                        ethTran.confirmCount = (uint)(index - ethTran.height + 1);
                    else
                    {
                        ethTran.confirmCount = 0;
                    }
                }
            }
        }       
       
    }


    public class BatchGetTransactionReceipt
    {
        protected string _url = "";
        protected string _data = "";

        protected WebClient wc;

        public BatchGetTransactionReceipt(string host)
        {
            _url = host;
            wc = new WebClient();
        }

        public void beginBuildPar()
        {
            _data = "[";
        }

        public void pushTxHash(string txHash)
        {
            _data += "{\"jsonrpc\":\"2.0\",\"method\":\"eth_getTransactionReceipt\",\"params\":[\"" + txHash + "\"],\"id\":1},";
        }

        public void endBuildPar()
        {
            _data = _data.Substring(0, _data.Length - 1);
            _data += "]";
        }

        public JToken doRequest()
        {
            wc.Headers["content-type"] = "application/json;charset=utf-8";
            byte[] bd = Encoding.UTF8.GetBytes(_data);
            byte[] retdata = wc.UploadData(_url, "POST", bd);
            return JToken.Parse(Encoding.UTF8.GetString(retdata));
        }

    }

}
