﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using Tzkt.Data.Models;
using Tzkt.Data.Models.Base;

namespace Tzkt.Sync.Protocols.Proto1
{
    class RevealsCommit : ProtocolCommit
    {
        public List<RevealOperation> Reveals { get; protected set; }
        public Dictionary<string, string> PubKeys { get; protected set; }

        public RevealsCommit(ProtocolHandler protocol, List<ICommit> commits) : base(protocol, commits) { }

        public override async Task Init(IBlock block)
        {
            var rawBlock = block as RawBlock;
            var parsedBlock = FindCommit<BlockCommit>().Block;

            Reveals = new List<RevealOperation>();
            PubKeys = new Dictionary<string, string>(4);
            foreach (var op in rawBlock.Operations[3])
            {
                foreach (var content in op.Contents.Where(x => x is RawRevealContent))
                {
                    var reveal = content as RawRevealContent;

                    PubKeys[reveal.Source] = reveal.PublicKey;

                    Reveals.Add(new RevealOperation
                    {
                        OpHash = op.Hash,
                        Block = parsedBlock,
                        Timestamp = parsedBlock.Timestamp,
                        BakerFee = reveal.Fee,
                        Counter = reveal.Counter,
                        GasLimit = reveal.GasLimit,
                        StorageLimit = reveal.StorageLimit,
                        Sender = await Accounts.GetAccountAsync(reveal.Source),
                        Status = reveal.Metadata.Result.Status switch
                        {
                            "applied" => OperationStatus.Applied,
                            _ => throw new NotImplementedException()
                        }
                    });
                }
            }
        }

        public override Task Apply()
        {
            foreach (var reveal in Reveals)
            {
                #region balances
                reveal.Block.Baker.FrozenFees += reveal.BakerFee;
                reveal.Sender.Balance -= reveal.BakerFee;
                #endregion

                #region counters
                reveal.Sender.Counter = Math.Max(reveal.Sender.Counter, reveal.Counter);
                reveal.Sender.Operations |= Operations.Reveals;
                reveal.Block.Operations |= Operations.Reveals;
                #endregion

                if (reveal.Sender is User user)
                    user.PublicKey = PubKeys[reveal.Sender.Address];

                if (Db.Entry(reveal.Sender).State != EntityState.Added)
                    Db.Accounts.Update(reveal.Sender);

                Db.Delegates.Update(reveal.Block.Baker);
                Db.RevealOps.Add(reveal);
            }

            return Task.CompletedTask;
        }

        public override async Task Revert()
        {
            foreach (var reveal in Reveals)
            {
                var block = await State.GetCurrentBlock();
                var baker = (Data.Models.Delegate)await Accounts.GetAccountAsync(block.BakerId.Value);
                var sender = await Accounts.GetAccountAsync(reveal.SenderId);

                #region balances
                baker.FrozenFees -= reveal.BakerFee;
                sender.Balance += reveal.BakerFee;
                #endregion

                #region counters
                if (!await Db.RevealOps.AnyAsync(x => x.Sender.Id == sender.Id && x.Id != reveal.Id))
                    sender.Operations &= ~Operations.Reveals;

                sender.Counter = Math.Min(sender.Counter, reveal.Counter - 1);
                #endregion

                if (sender is User user)
                    user.PublicKey = null;

                if (sender.Operations == Operations.None && sender.Counter > 0)
                    Db.Accounts.Remove(sender);
                else
                    Db.Accounts.Update(sender);

                Db.Delegates.Update(baker);
                Db.RevealOps.Remove(reveal);
            }
        }

        #region static
        public static async Task<RevealsCommit> Create(ProtocolHandler protocol, List<ICommit> commits, RawBlock rawBlock)
        {
            var commit = new RevealsCommit(protocol, commits);
            await commit.Init(rawBlock);
            return commit;
        }

        public static Task<RevealsCommit> Create(ProtocolHandler protocol, List<ICommit> commits, List<RevealOperation> reveals)
        {
            var commit = new RevealsCommit(protocol, commits) { Reveals = reveals };
            return Task.FromResult(commit);
        }
        #endregion
    }
}