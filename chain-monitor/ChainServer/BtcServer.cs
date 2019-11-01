using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using ChainMonitor.Helper;
using log4net;
using NBitcoin;

namespace ChainMonitor
{
    public class BtcServer
    {
        private static List<TransactionInfo> btcTransRspList = new List<TransactionInfo>(); //BTC 交易列表
        private static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
       
        public static void Start()
        {
            ulong btcStartHeight = Program.btcStartHeight;
            DbHelper.GetTransList(ref btcTransRspList, Config._confirmCountDict["btc"], "btc");
            Logger.Info("Btc watcher start! index: " + btcStartHeight);

            var key = new System.Net.NetworkCredential("1", "1");
            var uri = new Uri(Config._apiDict["btc"]);
            NBitcoin.RPC.RPCClient rpcC = new NBitcoin.RPC.RPCClient(key, uri);

            while (true)
            {
                try
                {
                    var count = (ulong)rpcC.GetBlockCount();

                    while (btcStartHeight < count)
                    {
                        ParseBtcBlock(rpcC, btcStartHeight);
                        DbHelper.SaveIndex(btcStartHeight, "btc");
                        
                        Logger.Info("Parse BTC Height:" + btcStartHeight);                        

                        btcStartHeight++;
                    }

                    Thread.Sleep(10000);
                }
                catch (Exception e)
                {
                    Logger.Error("btc: " + e.Message);
                    Logger.Error("stack: " + e.StackTrace);                    
                }

            }
        }

        /// <summary>
        /// 解析比特币区块
        /// </summary>
        /// <param name="rpcC"></param>
        /// <param name="index">被解析区块</param>
        /// <param name="height">区块高度</param>
        /// <returns></returns>
        private static void ParseBtcBlock(NBitcoin.RPC.RPCClient rpcC, ulong index)
        {
            var block = rpcC.GetBlockAsync((int)index).Result;

            if (block.Transactions.Count > 0 && Config._btcAddrList.Count > 0)
            {
                for (var i = 0; i < block.Transactions.Count; i++)
                {
                    var tran = block.Transactions[i];
                    var txid = tran.GetHash().ToString();

                    //如果存在该 txid 了，说明这笔交易已经解析过了
                    if (btcTransRspList.Exists(x => x.txid == txid))
                        continue;
                    for (var vo = 0; vo < tran.Outputs.Count; vo++)
                    {
                        var vout = tran.Outputs[vo];
                        var address = vout.ScriptPubKey.GetDestinationAddress(Config._nettype); //比特币地址和网络有关

                        for (int j = 0; j < Config._btcAddrList.Count; j++)
                        {
                            if (address?.ToString() == Config._btcAddrList[j])
                            {
                                var btcTrans = new TransactionInfo();
                                btcTrans.coinType = "btc";
                                btcTrans.toAddress = address.ToString();
                                btcTrans.value = vout.Value.ToDecimal(MoneyUnit.BTC);
                                btcTrans.confirmCount = 1;
                                btcTrans.height = index;
                                btcTrans.txid = txid;

                                btcTransRspList.Add(btcTrans);
                                Logger.Info(index + " Have A BTC Transaction To:" + address + "; Value:" + btcTrans.value + "; Txid:" + btcTrans.txid);
                            }
                        }
                    }
                }
            }

            if (btcTransRspList.Count > 0)
            {
                //更新确认次数
                CheckBtcConfirm(btcTransRspList, index, rpcC);
                //发送和保存交易信息
                TransSender.SendTransTimer(btcTransRspList);
                //移除确认次数为 设定数量 和 0 的交易
                btcTransRspList.RemoveAll(x => x.confirmCount >= Config._confirmCountDict["btc"] || x.confirmCount == 0);
            }
        }

        /// <summary>
        /// 检查 BTC 确认次数
        /// </summary>
        /// <param name="num">需确认次数</param>
        /// <param name="btcTransRspList">交易列表</param>
        /// <param name="index">当前解析区块</param>
        /// <param name="rpcC"></param>
        private static void CheckBtcConfirm(List<TransactionInfo> btcTransRspList, ulong index, NBitcoin.RPC.RPCClient rpcC)
        {
            foreach (var btcTran in btcTransRspList)
            {
                if (index > btcTran.height)
                {
                    var block = rpcC.GetBlockAsync((int)btcTran.height).Result;
                    //如果原区块中还包含该交易，则确认数 = 当前区块高度 - 交易所在区块高度 + 1，不包含该交易，确认数统一记为 0
                    if (block.Transactions.Count > 0 && block.Transactions.Exists(x => x.GetHash().ToString() == btcTran.txid))
                        btcTran.confirmCount = (uint)(index - btcTran.height + 1);
                    else
                    {
                        btcTran.confirmCount = 0;
                    }
                }
            }
        }
       
    }
}
