using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Zoro;
using Zoro.Cryptography;
using Zoro.Cryptography.ECC;
using Zoro.Network.P2P.Payloads;
using Zoro.SmartContract;
using Zoro.Wallets;
using Zoro.IO;
using Neo.VM;
using System.Numerics;

namespace ChainMonitor.Helper
{
    class ZoroHelper
    {
        private static string api = Config._apiDict["zoro"];

        public static UInt160 Parse(string value)
        {
            if (value.StartsWith("0x"))
                value = value.Substring(2);
            if (value.Length != 40)
                return UInt160.Zero;
            return new UInt160(value.HexToBytes().Reverse().ToArray());
        }

        public static KeyPair GetKeyPairFromWIF(string wif)
        {
            byte[] prikey = Wallet.GetPrivateKeyFromWIF(wif);
            KeyPair keypair = new KeyPair(prikey);
            return keypair;
        }

        public static byte[] GetPrivateKeyFromWIF(string wif)
        {
            byte[] prikey = Wallet.GetPrivateKeyFromWIF(wif);
            return prikey;
        }

        public static ECPoint GetPublicKeyFromPrivateKey(byte[] prikey)
        {
            ECPoint pubkey = ECCurve.Secp256r1.G * prikey;
            return pubkey;
        }

        public static UInt160 GetPublicKeyHash(ECPoint pubkey)
        {
            UInt160 script_hash = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            return script_hash;
        }

        public static UInt160 GetPublicKeyHashFromWIF(string WIF)
        {
            byte[] prikey = GetPrivateKeyFromWIF(WIF);
            ECPoint pubkey = GetPublicKeyFromPrivateKey(prikey);
            return GetPublicKeyHash(pubkey);
        }

        public static UInt160 GetPublicKeyHashFromAddress(string address)
        {
            System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
            var alldata = Base58.Decode(address);
            var data = alldata.Take(alldata.Length - 4).ToArray();
            var hash = sha256.ComputeHash(data);
            hash = sha256.ComputeHash(hash);
            var hashbts = hash.Take(4).ToArray();
            var datahashbts = alldata.Skip(alldata.Length - 4).ToArray();
            var pkhash = data.Skip(1).ToArray();
            return new UInt160(pkhash);
        }

        public static string GetAddressFromScriptHash(UInt160 scripthash)
        {
            System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] data = new byte[20 + 1];
            data[0] = 0x17;
            Array.Copy(scripthash.ToArray(), 0, data, 1, 20);
            var hash = sha256.ComputeHash(data);
            hash = sha256.ComputeHash(hash);

            var alldata = data.Concat(hash.Take(4)).ToArray();

            return Base58.Encode(alldata);
        }

        public static string GetHexStringFromAddress(string address)
        {
            System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
            var alldata = Base58.Decode(address);
            var data = alldata.Take(alldata.Length - 4).ToArray();
            var hash = sha256.ComputeHash(data);
            hash = sha256.ComputeHash(hash);
            var hashbts = hash.Take(4).ToArray();
            var datahashbts = alldata.Skip(alldata.Length - 4).ToArray();
            var pkhash = data.Skip(1).ToArray();
            return pkhash.ToHexString();
        }

        public static string GetJsonAddress(JObject item)
        {
            var type = item["type"].ToString();
            var value = item["value"].ToString(); ;
            if (type == "ByteArray" && !string.IsNullOrEmpty(value))
            {
                return GetAddressFromScriptHash(HexString2Bytes(value));
            }
            return "";
        }

        public static decimal GetJsonDecimal(JObject item, int decimals)
        {
            var type = item["type"].ToString();
            var value = item["value"];
            if (type == "ByteArray")
            {
                var bt = new BigInteger(ZoroHelper.HexString2Bytes(value.ToString()));
                return (decimal)bt / (decimal)Math.Pow(10, decimals);
            }
            else if (type == "Integer")
            {
                return decimal.Parse(value.ToString()) / (decimal)Math.Pow(10, decimals);
            }
            return 0;
        }

        public static UInt160 GetMultiSigRedeemScriptHash(int m, KeyPair[] keypairs)
        {
            return Contract.CreateMultiSigRedeemScript(m, keypairs.Select(p => p.PublicKey).ToArray()).ToScriptHash();
        }

        public static byte[] Sign(byte[] data, byte[] prikey, ECPoint pubkey)
        {
            return Crypto.Default.Sign(data, prikey, pubkey.EncodePoint(false).Skip(1).ToArray());
        }

        public static byte[] GetHashData(IVerifiable verifiable)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                verifiable.SerializeUnsigned(writer);
                writer.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] GetRawData(IVerifiable verifiable)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                verifiable.Serialize(writer);
                writer.Flush();
                return ms.ToArray();
            }
        }

        public static void AddWitness(Transaction tx, byte[][] signdata, int m, ECPoint[] pubkeys)
        {
            var vscript = Contract.CreateMultiSigRedeemScript(m, pubkeys).ToArray();

            using (var sb = new ScriptBuilder())
            {
                int i = 0;
                foreach (var sig in signdata)
                {
                    sb.EmitPush(sig);
                    if (++i >= m)
                        break;
                }

                var iscript = sb.ToArray();

                AddWitness(tx, vscript, iscript);
            }
        }

        public static void AddWitness(Transaction tx, byte[] signdata, ECPoint pubkey)
        {
            var vscript = Contract.CreateSignatureRedeemScript(pubkey).ToArray();

            using (var sb = new ScriptBuilder())
            {
                sb.EmitPush(signdata);

                var iscript = sb.ToArray();

                AddWitness(tx, vscript, iscript);
            }
        }

        public static void AddWitness(Transaction tx, byte[] vscript, byte[] iscript)
        {
            List<Witness> wit = null;
            if (tx.Witnesses == null)
            {
                wit = new List<Witness>();
            }
            else
            {
                wit = new List<Witness>(tx.Witnesses);
            }
            Witness newwit = new Witness();
            newwit.VerificationScript = vscript;
            newwit.InvocationScript = iscript;
            foreach (var w in wit)
            {
                if (w.ScriptHash == newwit.ScriptHash)
                    throw new Exception("alread have this witness");
            }

            wit.Add(newwit);
            tx.Witnesses = wit.ToArray();
        }

        public static byte[] HexString2Bytes(string str)
        {
            if (str.IndexOf("0x") == 0)
                str = str.Substring(2);
            byte[] outd = new byte[str.Length / 2];
            for (var i = 0; i < str.Length / 2; i++)
            {
                outd[i] = byte.Parse(str.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
            }
            return outd;
        }

        private static string Bytes2HexString(byte[] data)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
            {
                sb.Append(d.ToString("x02"));
            }
            return sb.ToString();
        }

        //将 byte[] 转成 string 类型的 address
        public static string GetAddressFromScriptHash(byte[] scripthash)
        {
            System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] data = new byte[20 + 1];
            data[0] = 0x17;
            Array.Copy(scripthash.ToArray(), 0, data, 1, 20);
            var hash = sha256.ComputeHash(data);
            hash = sha256.ComputeHash(hash);

            var alldata = data.Concat(hash.Take(4)).ToArray();

            return Base58.Encode(alldata);
        }

        public static int GetHeight()
        {
            string url = api + "?jsonrpc=2.0&id=1&method=getblockcount&params=['']";
            var result = Helper.HttpGet(url);
            return int.Parse(JObject.Parse(result)["result"].ToString());
        }

        public static string SendRawTransaction(string rawdata, string chainHash)
        {
            byte[] postdata;
            string url;

            JArray postRawArray = new JArray();
            postRawArray.Add(chainHash);
            postRawArray.Add(rawdata);

            url = Helper.MakeRpcUrlPost(api, "sendrawtransaction", out postdata, postRawArray);

            var result = Helper.HttpPost(url, postdata);

            return result;
        }


        public static string InvokeScript(byte[] script, string chainHash)
        {
            byte[] postdata;
            string url;

            string scriptPublish = script.ToHexString();
            JArray postArray = new JArray();
            postArray.Add(chainHash);
            postArray.Add(scriptPublish);

            url = Helper.MakeRpcUrlPost(api, "invokescript", out postdata, postArray);

            var result = Helper.HttpPost(url, postdata);

            return result;
        }

        public static decimal GetScriptGasConsumed(byte[] script, string chainHash)
        {
            var info = InvokeScript(script, chainHash);

            JObject json_result_array = JObject.Parse(info);
            JObject json_result_obj = json_result_array["result"] as JObject;

            var consume = json_result_obj["gas_consumed"].ToString();
            return decimal.Parse(consume);
        }

        public static string GetJsonValue(JObject item)
        {
            var type = item["type"].ToString();
            var value = item["value"];
            if (type == "ByteArray")
            {
                var bt = HexString2Bytes(value.ToString());
                var num = new BigInteger(bt);
                return num.ToString();

            }
            else if (type == "Integer")
            {
                return value.ToString();
            }
            return "";
        }

        public static InvocationTransaction MakeTransaction(byte[] script, KeyPair keypair, Fixed8 gasLimit, Fixed8 gasPrice)
        {
            InvocationTransaction tx = new InvocationTransaction
            {
                Nonce = Transaction.GetNonce(),
                Script = script,
                GasPrice = gasPrice,
                GasLimit = gasLimit.Ceiling(),
                Account = GetPublicKeyHash(keypair.PublicKey)
            };

            tx.Attributes = new TransactionAttribute[0];

            byte[] data = GetHashData(tx);
            byte[] signdata = Sign(data, keypair.PrivateKey, keypair.PublicKey);
            AddWitness(tx, signdata, keypair.PublicKey);

            return tx;
        }

        public static InvocationTransaction MakeMultiSignatureTransaction(byte[] script, int m, KeyPair[] keypairs, Fixed8 gasLimit, Fixed8 gasPrice)
        {
            InvocationTransaction tx = new InvocationTransaction
            {
                Nonce = Transaction.GetNonce(),
                Script = script,
                GasPrice = gasPrice,
                GasLimit = gasLimit.Ceiling(),
                Account = GetMultiSigRedeemScriptHash(m, keypairs)
            };

            int count = keypairs.Length;
            ECPoint[] pubkeys = keypairs.Select(p => p.PublicKey).ToArray();

            tx.Attributes = new TransactionAttribute[0];

            byte[] data = GetHashData(tx);
            byte[][] signatures = new byte[count][];

            int i = 0;
            foreach (KeyPair keypair in keypairs.OrderBy(p => p.PublicKey))
            {
                signatures[i++] = Sign(data, keypair.PrivateKey, keypair.PublicKey);
            }

            AddWitness(tx, signatures, m, pubkeys);
            return tx;
        }

        public static string SendInvocationTransaction(byte[] script, int m, KeyPair[] keypairs, string chainHash, Fixed8 gasLimit, Fixed8 gasPrice)
        {
            InvocationTransaction tx = MakeMultiSignatureTransaction(script, m, keypairs, gasLimit, gasPrice);

            return SendRawTransaction(tx.ToArray().ToHexString(), chainHash);
        }

        public static decimal EstimateGas(Transaction tx, string chainHash)
        {
            var response = PostHttpRequest(api, "estimategas", tx.ToArray().ToHexString(), chainHash);
            JObject json_response = JObject.Parse(response);
            JObject json_result = json_response["result"] as JObject;
            string json_gas_consumed = json_result["gas_consumed"].ToString();
            decimal gas_consumed = decimal.Parse(json_gas_consumed);
            return gas_consumed;
        }

        public static string PostHttpRequest(string api, string method, string rawdata, string chainHash)
        {
            string url;
            byte[] postdata;

            JArray postRawArray = new JArray();
            postRawArray.Add(chainHash);
            postRawArray.Add(rawdata);

            url = Helper.MakeRpcUrlPost(api, method, out postdata, postRawArray);

            string result = "";

            result = Helper.HttpPost(url, postdata);
            return result;
        }
        //如果参数为string,其实是特殊值
        //(string) or(str) 开头，表示是个字符串，utf8编码为bytes
        //(bytes) or([])开头，表示就是一个bytearray
        //(address) or(addr)开头，表示是一个地址，转换为脚本hash
        //(integer) or(int) 开头，表示是一个大整数
        //(hexinteger) or (hexint) or (hex) 开头，表示是一个16进制表示的大整数，转换为bytes就是反序
        //(int256) or (hex256) 开头,表示是一个定长的256位 16进制大整数
        //(int160) or (hex160) 开头,表示是一个定长的160位 16进制大整数
        public static byte[] GetParamBytes(string str)
        {
            if (str[0] != '(')
                throw new Exception("must start with:(string) or (bytes) or (address) or (hexint) or (int) or (int256) or (int160)");

            if (str.IndexOf("(str)") == 0)
            {
                return Encoding.UTF8.GetBytes(str.Substring(5));
            }
            else if (str.IndexOf("(string)") == 0)
            {
                return Encoding.UTF8.GetBytes(str.Substring(8));
            }
            //(bytes) or([])开头，表示就是一个bytearray
            else if (str.IndexOf("(bytes)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(7));
                return hex;
            }
            else if (str.IndexOf("([])") == 0)
            {
                var hex = HexString2Bytes(str.Substring(4));
                return hex;
            }
            //(address) or(addr)开头，表示是一个地址，转换为脚本hash
            else if (str.IndexOf("(address)") == 0)
            {
                var addr = (str.Substring(9));
                var hex = GetPublicKeyHashFromAddress(addr);
                return hex.ToArray();
            }
            else if (str.IndexOf("(addr)") == 0)
            {
                var addr = (str.Substring(6));
                var hex = GetPublicKeyHashFromAddress(addr);
                return hex.ToArray();
            }
            //(integer) or(int) 开头，表示是一个大整数
            else if (str.IndexOf("(integer)") == 0)
            {
                var num = BigInteger.Parse(str.Substring(9));
                return num.ToByteArray();
            }
            else if (str.IndexOf("(int)") == 0)
            {
                var num = BigInteger.Parse(str.Substring(5));
                return num.ToByteArray();
            }
            //(hexinteger) or (hexint) or (hex) 开头，表示是一个16进制表示的大整数，转换为bytes就是反序
            else if (str.IndexOf("(hexinteger)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(12));
                return hex.Reverse().ToArray();
            }
            else if (str.IndexOf("(hexint)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(8));
                return hex.Reverse().ToArray();
            }
            else if (str.IndexOf("(hex)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(5));
                return hex.Reverse().ToArray();
            }
            //(int256) or (hex256) 开头,表示是一个定长的256位 16进制大整数
            else if (str.IndexOf("(hex256)") == 0 || str.IndexOf("(int256)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(8));
                if (hex.Length != 32)
                    throw new Exception("error lenght");
                return hex.Reverse().ToArray();
            }
            else if (str.IndexOf("(uint256)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(9));
                if (hex.Length != 32)
                    throw new Exception("error lenght");
                return (hex.Reverse().ToArray());
            }
            //(int160) or (hex160) 开头,表示是一个定长的160位 16进制大整数
            else if (str.IndexOf("(hex160)") == 0 || str.IndexOf("(int160)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(8));
                if (hex.Length != 20)
                    throw new Exception("error lenght");
                return (hex.Reverse().ToArray());
            }
            else if (str.IndexOf("(uint160)") == 0)
            {
                var hex = HexString2Bytes(str.Substring(9));
                if (hex.Length != 20)
                    throw new Exception("error lenght");
                return (hex.Reverse().ToArray());
            }
            else
                throw new Exception("must start with:(str) or (hex) or (hexbig) or (int)");
        }
    }
}
