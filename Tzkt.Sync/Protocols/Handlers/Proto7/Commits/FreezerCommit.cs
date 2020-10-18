﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tzkt.Data.Models;

namespace Tzkt.Sync.Protocols.Proto7
{
    class FreezerCommit : ProtocolCommit
    {
        public IEnumerable<IBalanceUpdate> FreezerUpdates { get; private set; }
        public Protocol Protocol { get; private set; }

        FreezerCommit(ProtocolHandler protocol) : base(protocol) { }

        public async Task Init(Block block, RawBlock rawBlock)
        {
            if (block.Events.HasFlag(BlockEvents.CycleEnd))
            {
                Protocol = await Cache.Protocols.GetAsync(rawBlock.Protocol);
                var cycle = (rawBlock.Level - 1) / Protocol.BlocksPerCycle;

                FreezerUpdates = rawBlock.Metadata.BalanceUpdates.Skip(Protocol.BlockReward0 == 0 || rawBlock.Operations[0].Count == 0 ? 2 : 3)
                    .Where(x => x is FreezerUpdate fu && fu.Cycle == cycle - Protocol.PreservedCycles);
            }
        }

        public async Task Init(Block block)
        {
            if (block.Events.HasFlag(BlockEvents.CycleEnd))
            {
                var stream = await Proto.Node.GetBlockAsync(block.Level);
                var rawBlock = (RawBlock)await (Proto.Serializer as Serializer).DeserializeBlock(stream);

                Protocol = await Cache.Protocols.GetAsync(rawBlock.Protocol);
                var cycle = (rawBlock.Level - 1) / Protocol.BlocksPerCycle;

                FreezerUpdates = rawBlock.Metadata.BalanceUpdates.Skip(Protocol.BlockReward0 == 0 || rawBlock.Operations[0].Count == 0 ? 2 : 3)
                    .Where(x => x is FreezerUpdate fu && fu.Cycle == cycle - Protocol.PreservedCycles);
            }
        }

        public override Task Apply()
        {
            if (FreezerUpdates == null) return Task.CompletedTask;

            foreach (var update in FreezerUpdates)
            {
                #region entities
                var delegat = Cache.Accounts.GetDelegate(update.Target);

                Db.TryAttach(delegat);
                #endregion

                if (update is DepositsUpdate depositsFreezer)
                {
                    delegat.FrozenDeposits += depositsFreezer.Change;
                }
                else if (update is RewardsUpdate rewardsFreezer)
                {
                    delegat.FrozenRewards += rewardsFreezer.Change;
                    delegat.StakingBalance -= rewardsFreezer.Change;
                }
                else if (update is FeesUpdate feesFreezer)
                {
                    delegat.FrozenFees += feesFreezer.Change;
                }
                else
                {
                    throw new Exception("unexpected freezer balance update type");
                }
            }

            return Task.CompletedTask;
        }

        public override Task Revert()
        {
            if (FreezerUpdates == null) return Task.CompletedTask;

            foreach (var update in FreezerUpdates)
            {
                #region entities
                var delegat = Cache.Accounts.GetDelegate(update.Target);

                Db.TryAttach(delegat);
                #endregion

                if (update is DepositsUpdate depositsFreezer)
                {
                    delegat.FrozenDeposits -= depositsFreezer.Change;
                }
                else if (update is RewardsUpdate rewardsFreezer)
                {
                    delegat.FrozenRewards -= rewardsFreezer.Change;
                    delegat.StakingBalance += rewardsFreezer.Change;
                }
                else if (update is FeesUpdate feesFreezer)
                {
                    delegat.FrozenFees -= feesFreezer.Change;
                }
                else
                {
                    throw new Exception("unexpected freezer balance update type");
                }
            }

            return Task.CompletedTask;
        }

        #region static
        public static async Task<FreezerCommit> Apply(ProtocolHandler proto, Block block, RawBlock rawBlock)
        {
            var commit = new FreezerCommit(proto);
            await commit.Init(block, rawBlock);
            await commit.Apply();

            return commit;
        }

        public static async Task<FreezerCommit> Revert(ProtocolHandler proto, Block block)
        {
            var commit = new FreezerCommit(proto);
            await commit.Init(block);
            await commit.Revert();

            return commit;
        }
        #endregion
    }
}