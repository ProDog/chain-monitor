using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Collections.Generic;
using System.Numerics;

namespace ChainMonitor
{
    public class TransactionInfo
    {
        public string netType = Config._nettype.Name;//网络  testnet  mainnet
        public string coinType; //币种
        public uint confirmCount;  //确认次数
        public ulong height; //高度
        public string toAddress;  //收款地址
        public string txid;  //txid
        public decimal value;  //金额
    }


    public class Utxo
    {
        //txid[n] 是utxo的属性
        public ThinNeo.Hash256 txid;
        public int n;
        //asset资产、addr 属于谁，value数额，这都是查出来的
        public string addr;
        public string asset;
        public decimal value;

        public Utxo(string _addr, ThinNeo.Hash256 _txid, string _asset, decimal _value, int _n)
        {
            this.addr = _addr;
            this.txid = _txid;
            this.asset = _asset;
            this.value = _value;
            this.n = _n;
        }
    }

    public class RspInfo
    {
        public bool state;
        public dynamic msg;
    }

    public class CoinInfon
    {
        public string coinType { get; set; }
        public decimal balance { get; set; }
    }

    public class TransResult
    {
        public TransError errorCode = TransError.NoError;
        public string coinType { get; set; }
        public string key { get; set; }
        public string txid { get; set; }
    }

    public class BlockHeight
    {
        public ulong zoroParseHeight;
        public ulong zoroBlockHeight;

        public ulong ethParseHeight;
        public ulong ethBlockHeight;
    }

    public enum TransError
    {
        NoError = 0,
        NotEnoughMoney = 1,
        TransError = 2
    }

    public class AccountInfo
    {
        public string coinType { get; set; }
        public string address { get; set; }
    }

    public class Erc20TransferLog
    {
        public string coinType { get; set; }        
        public string toAddress;  //收款地址
        public string txid;  //txid
        public BigInteger value;  //金额
    }

    public class BlockState
    {
        public string Chain;
        public string ContractHash;
        public uint BlockTime;
        public ulong BlockHeight;
        public List<TransState> TransStates;
    }

    public class TransState
    {
        public string Txid;
        public bool VmState;
        public List<WithdrawInfo> Notifications;
    }

    public class WithdrawInfo
    {
        public string Method;
        public string CoinType;
        public string FromAddress;
        public string ReceiveAddress;
        public decimal Value;
    }

    [Function("balanceOf", "uint256")]
    public class BalanceOfFunction : FunctionMessage
    {

        [Parameter("address", "_owner", 1)]
        public string Owner { get; set; }

    }
}
