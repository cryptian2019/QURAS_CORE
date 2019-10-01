using Pure.Cryptography;
using Pure.IO;
using Pure.IO.Json;
using Pure.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pure.Core
{
    public class Block : BlockBase, IInventory, IEquatable<Block>
    {
        public Transaction[] Transactions;

        private Header _header = null;

        public Header Header
        {
            get
            {
                if (_header == null)
                {
                    _header = new Header
                    {
                        PrevHash = PrevHash,
                        MerkleRoot = MerkleRoot,
                        Timestamp = Timestamp,
                        Index = Index,
                        ConsensusData = ConsensusData,
                        NextConsensus = NextConsensus,
                        Script = Script
                    };
                }
                return _header;
            }
        }

        InventoryType IInventory.InventoryType => InventoryType.Block;

        public override int Size => base.Size + Transactions.GetVarSize();

        public static Dictionary<UInt256, Fixed8> CalculateNetFee(IEnumerable<Transaction> transactions)
        {
            Dictionary<UInt256, Fixed8> ret = new Dictionary<UInt256, Fixed8>();
            #region Calculate QRG fee
            {
                Transaction[] ts = transactions.Where(p => p.Type != TransactionType.MinerTransaction && p.Type != TransactionType.ClaimTransaction && p.Type != TransactionType.AnonymousContractTransaction).ToArray();
                Fixed8 amount_in = ts.SelectMany(p => p.References.Values.Where(o => o.AssetId == Blockchain.UtilityToken.Hash)).Sum(p => p.Value);
                Fixed8 amount_out = ts.SelectMany(p => p.Outputs.Where(o => o.AssetId == Blockchain.UtilityToken.Hash)).Sum(p => p.Value);
                Fixed8 amount_sysfee = ts.Sum(p => p.SystemFee);
                Fixed8 normalFee = amount_in - amount_out;

                Transaction[] ats = transactions.Where(p => p.Type == TransactionType.AnonymousContractTransaction && p.Inputs.Length == 0).ToArray();
                Fixed8 v_pubNewSum = Fixed8.Zero;
                Fixed8 outAmount = ats.SelectMany(p => p.Outputs.Where(o => o.AssetId == Blockchain.UtilityToken.Hash)).Sum(p => p.Value);

                foreach (var atx in ats)
                {
                    if (atx is AnonymousContractTransaction)
                    {
                        var formatedTx = atx as AnonymousContractTransaction;
                        for (int i = 0; i < formatedTx.byJoinSplit.Count; i++)
                        {
                            if (formatedTx.Asset_ID(i) == Blockchain.UtilityToken.Hash)
                            {
                                v_pubNewSum += formatedTx.vPub_New(i);
                            }
                        }
                    }
                }

                Fixed8 anonymousFee = v_pubNewSum - outAmount;
                Fixed8 totalQrgFee = normalFee + anonymousFee;
                Transaction[] feeTxs = transactions.Where(p => p.Type != TransactionType.MinerTransaction && p.Type != TransactionType.ClaimTransaction).ToArray();

                foreach (var tx in feeTxs)
                {
                    Dictionary<UInt256, Fixed8> fee = new Dictionary<UInt256, Fixed8>();
                    if (tx.Type != TransactionType.AnonymousContractTransaction)
                    {
                        foreach (var txOut in tx.Outputs)
                        {
                            if (txOut.AssetId != Blockchain.GoverningToken.Hash)
                            {
                                if (!fee.ContainsKey(txOut.AssetId))
                                {
                                    AssetState asset = Blockchain.Default.GetAssetState(txOut.AssetId);
                                    fee[txOut.AssetId] = asset.Fee;
                                }
                            }
                        }
                    }
                    else if (tx.Type == TransactionType.AnonymousContractTransaction && tx.Inputs.Length > 0)
                    {
                        var atx = tx as AnonymousContractTransaction;
                        
                        for (int i = 0; i < atx.byJoinSplit.Count; i++)
                        {
                            if (atx.Asset_ID(i) != Blockchain.GoverningToken.Hash)
                            {
                                if (!fee.ContainsKey(atx.Asset_ID(i)))
                                {
                                    AssetState asset = Blockchain.Default.GetAssetState(atx.Asset_ID(i));
                                    fee[asset.AssetId] = asset.Fee;
                                }
                            }
                        }
                    }
                    else
                    {
                        var atx = tx as AnonymousContractTransaction;

                        for (int i = 0; i < atx.byJoinSplit.Count; i++)
                        {
                            if (atx.Asset_ID(i) != Blockchain.GoverningToken.Hash)
                            {
                                if (!fee.ContainsKey(atx.Asset_ID(i)))
                                {
                                    AssetState asset = Blockchain.Default.GetAssetState(atx.Asset_ID(i));
                                    fee[asset.AssetId] = asset.AFee;
                                }
                            }
                        }
                    }

                    foreach (var key in fee.Keys)
                    {
                        if (ret.ContainsKey(key))
                        {
                            ret[key] += fee[key];
                        }
                        else
                        {
                            ret[key] = fee[key];
                        }
                    }
                }

                Fixed8 qrgAssetFee = ret.Sum(p => p.Value);

                if (ret.ContainsKey(Blockchain.UtilityToken.Hash))
                {
                    ret[Blockchain.UtilityToken.Hash] = ret[Blockchain.UtilityToken.Hash] + totalQrgFee - qrgAssetFee;
                }
                else
                {
                    ret[Blockchain.UtilityToken.Hash] = totalQrgFee - qrgAssetFee;
                }
            }

            #endregion

            #region Calculate QRS Fee
            {
                Transaction[] ts = transactions.Where(p => p.Type != TransactionType.MinerTransaction && p.Type != TransactionType.ClaimTransaction && p.Type != TransactionType.AnonymousContractTransaction).ToArray();
                Fixed8 amount_in = ts.SelectMany(p => p.References.Values.Where(o => o.AssetId == Blockchain.GoverningToken.Hash)).Sum(p => p.Value);
                Fixed8 amount_out = ts.SelectMany(p => p.Outputs.Where(o => o.AssetId == Blockchain.GoverningToken.Hash)).Sum(p => p.Value);
                Fixed8 amount_sysfee = ts.Sum(p => p.SystemFee);
                Fixed8 normalFee = amount_in - amount_out;

                Transaction[] ats = transactions.Where(p => p.Type == TransactionType.AnonymousContractTransaction && p.Inputs.Length == 0).ToArray();
                Fixed8 v_pubNewSum = Fixed8.Zero;
                Fixed8 outAmount = ats.SelectMany(p => p.Outputs.Where(o => o.AssetId == Blockchain.GoverningToken.Hash)).Sum(p => p.Value);

                foreach (var atx in ats)
                {
                    if (atx is AnonymousContractTransaction)
                    {
                        var formatedTx = atx as AnonymousContractTransaction;
                        for (int i = 0; i < formatedTx.byJoinSplit.Count; i++)
                        {
                            if (formatedTx.Asset_ID(i) == Blockchain.GoverningToken.Hash)
                            {
                                v_pubNewSum += formatedTx.vPub_New(i);
                            }
                        }
                    }
                }

                Fixed8 anonymousFee = v_pubNewSum - outAmount;
                Fixed8 totalQrsFee = normalFee + anonymousFee;
                Transaction[] feeTxs = transactions.Where(p => p.Type != TransactionType.MinerTransaction && p.Type != TransactionType.ClaimTransaction).ToArray();

                foreach (var tx in feeTxs)
                {
                    Dictionary<UInt256, Fixed8> fee = new Dictionary<UInt256, Fixed8>();
                    if (tx.Type != TransactionType.AnonymousContractTransaction)
                    {
                        foreach (var txOut in tx.Outputs)
                        {
                            if (txOut.AssetId == Blockchain.GoverningToken.Hash)
                            {
                                if (!fee.ContainsKey(txOut.AssetId))
                                {
                                    AssetState asset = Blockchain.Default.GetAssetState(txOut.AssetId);
                                    fee[txOut.AssetId] = asset.Fee;
                                }
                            }
                        }
                    }
                    else if (tx.Type == TransactionType.AnonymousContractTransaction && tx.Inputs.Length > 0)
                    {
                        var atx = tx as AnonymousContractTransaction;

                        for (int i = 0; i < atx.byJoinSplit.Count; i++)
                        {
                            if (atx.Asset_ID(i) == Blockchain.GoverningToken.Hash)
                            {
                                if (!fee.ContainsKey(atx.Asset_ID(i)))
                                {
                                    AssetState asset = Blockchain.Default.GetAssetState(atx.Asset_ID(i));
                                    fee[asset.AssetId] = asset.Fee;
                                }
                            }
                        }
                    }
                    else
                    {
                        var atx = tx as AnonymousContractTransaction;

                        for (int i = 0; i < atx.byJoinSplit.Count; i++)
                        {
                            if (atx.Asset_ID(i) == Blockchain.GoverningToken.Hash)
                            {
                                if (!fee.ContainsKey(atx.Asset_ID(i)))
                                {
                                    AssetState asset = Blockchain.Default.GetAssetState(atx.Asset_ID(i));
                                    fee[asset.AssetId] = asset.AFee;
                                }
                            }
                        }
                    }

                    foreach (var key in fee.Keys)
                    {
                        if (ret.ContainsKey(key))
                        {
                            ret[key] += fee[key];
                        }
                        else
                        {
                            ret[key] = fee[key];
                        }
                    }
                }

                Fixed8 qrsAssetFee = ret.Sum(p => p.Value);

                if (ret.ContainsKey(Blockchain.GoverningToken.Hash))
                {
                    ret[Blockchain.GoverningToken.Hash] = ret[Blockchain.UtilityToken.Hash] + totalQrsFee - qrsAssetFee;
                }
                else
                {
                    ret[Blockchain.GoverningToken.Hash] = totalQrsFee - qrsAssetFee;
                }
            }
            #endregion

            Dictionary<UInt256, Fixed8> retFees = new Dictionary<UInt256, Fixed8>();
            foreach (var key in ret.Keys)
            {
                if (ret[key] > Fixed8.Zero)
                {
                    retFees[key] = ret[key];
                }
            }

            return retFees;
        }

        public override void Deserialize(BinaryReader reader)
        {
            base.Deserialize(reader);
            Transactions = new Transaction[reader.ReadVarInt(0x10000)];
            if (Transactions.Length == 0) throw new FormatException();
            for (int i = 0; i < Transactions.Length; i++)
            {
                Transactions[i] = Transaction.DeserializeFrom(reader);
            }
            if (MerkleTree.ComputeRoot(Transactions.Select(p => p.Hash).ToArray()) != MerkleRoot)
                throw new FormatException();
        }

        public bool Equals(Block other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (ReferenceEquals(null, other)) return false;
            return Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Block);
        }

        public static Block FromTrimmedData(byte[] data, int index, Func<UInt256, Transaction> txSelector)
        {
            Block block = new Block();
            using (MemoryStream ms = new MemoryStream(data, index, data.Length - index, false))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                ((IVerifiable)block).DeserializeUnsigned(reader);
                reader.ReadByte(); block.Script = reader.ReadSerializable<Witness>();
                block.Transactions = new Transaction[reader.ReadVarInt(0x10000000)];
                for (int i = 0; i < block.Transactions.Length; i++)
                {
                    block.Transactions[i] = txSelector(reader.ReadSerializable<UInt256>());
                }
            }
            return block;
        }

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }

        public void RebuildMerkleRoot()
        {
            MerkleRoot = MerkleTree.ComputeRoot(Transactions.Select(p => p.Hash).ToArray());
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);
            writer.Write(Transactions);
        }

        public override JObject ToJson()
        {
            JObject json = base.ToJson();
            json["tx"] = Transactions.Select(p => p.ToJson()).ToArray();
            return json;
        }

        public byte[] Trim()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                ((IVerifiable)this).SerializeUnsigned(writer);
                writer.Write((byte)1); writer.Write(Script);
                writer.Write(Transactions.Select(p => p.Hash).ToArray());
                writer.Flush();
                return ms.ToArray();
            }
        }

        public bool Verify(bool completely)
        {
            if (!Verify()) return false;
            if (Transactions[0].Type != TransactionType.MinerTransaction || Transactions.Skip(1).Any(p => p.Type == TransactionType.MinerTransaction))
                return false;
            if (completely)
            {
                if (NextConsensus != Blockchain.GetConsensusAddress(Blockchain.Default.GetValidators(Transactions).ToArray()))
                    return false;
                foreach (Transaction tx in Transactions)
                    if (!tx.Verify(Transactions.Where(p => !p.Hash.Equals(tx.Hash)))) return false;
                Transaction tx_gen = Transactions.FirstOrDefault(p => p.Type == TransactionType.MinerTransaction);

                Dictionary<UInt256, Fixed8> assetFee = CalculateNetFee(Transactions);

                foreach (var key in assetFee.Keys)
                {
                    AssetState asset = Blockchain.Default.GetAssetState(key);

                    if (asset.AssetId == Blockchain.GoverningToken.Hash)
                    {
                        if (tx_gen?.Outputs.Where(p => p.AssetId == Blockchain.GoverningToken.Hash).Sum(p => p.Value) != assetFee[key]) return false;
                    }
                    else if (asset.AssetId == Blockchain.UtilityToken.Hash)
                    {
                        if (tx_gen?.Outputs.Where(p => p.AssetId == Blockchain.UtilityToken.Hash).Sum(p => p.Value) != assetFee.Where(p => p.Key != Blockchain.GoverningToken.Hash).Sum(p => p.Value)) return false;
                    }
                    else
                    {
                        Fixed8 consensusFee = Fixed8.Zero;
                        Fixed8 assetOwnerFee = Fixed8.Zero;

                        if (assetFee[key] <= Fixed8.One)
                        {
                            consensusFee = assetFee[key] * 3 / 10;
                            assetOwnerFee = assetFee[key] * 7 / 10;
                        }
                        else if (assetFee[key] > Fixed8.One * 1)
                        {
                            consensusFee = assetFee[key] * 4 / 10;
                            assetOwnerFee = assetFee[key] * 6 / 10;
                        }

                        if (tx_gen?.Outputs.Where(p => p.ScriptHash == asset.FeeAddress).Sum(p => p.Value) != assetOwnerFee) return false;
                    }
                }
                // if (tx_gen?.Outputs.Sum(p => p.Value) != CalculateNetFee(Transactions)) return false;
            }
            return true;
        }
    }
}
