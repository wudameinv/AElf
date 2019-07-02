﻿using System;
using System.Collections.Generic;
using System.Linq;
using AElf.Contracts.MultiToken.Messages;
using AElf.Contracts.Profit;
using AElf.Contracts.Vote;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Election
{
    public partial class ElectionContract : ElectionContractContainer.ElectionContractBase
    {
        /// <summary>
        /// Initialize the ElectionContract and corresponding contract states.
        /// </summary>
        /// <param name="input">InitialElectionContractInput</param>
        /// <returns></returns>
        public override Empty InitialElectionContract(InitialElectionContractInput input)
        {
            Assert(!State.Initialized.Value, "Already initialized.");

            State.Candidates.Value = new PubkeyList();
            State.BlackList.Value = new PubkeyList();

            State.MinimumLockTime.Value = input.MinimumLockTime;
            State.MaximumLockTime.Value = input.MaximumLockTime;

            State.TimeEachTerm.Value = input.TimeEachTerm;

            State.MinerIncreaseInterval.Value = input.MinerIncreaseInterval;

            State.MinersCount.Value = input.MinerList.Count;
            State.InitialMiners.Value = new PubkeyList
            {
                Value = {input.MinerList.Select(k => k.ToByteString())}
            };
            foreach (var publicKey in input.MinerList)
            {
                State.CandidateInformationMap[publicKey] = new CandidateInformation {Pubkey = publicKey};
            }

            State.CurrentTermNumber.Value = 1;

            State.Initialized.Value = true;
            return new Empty();
        }

        public override Empty RegisterElectionVotingEvent(Empty input)
        {
            Assert(!State.VotingEventRegistered.Value, "Already registered.");

            State.VoteContract.Value = Context.GetContractAddressByName(SmartContractConstants.VoteContractSystemName);

            var votingRegisterInput = new VotingRegisterInput
            {
                IsLockToken = false,
                AcceptedCurrency = Context.Variables.NativeSymbol,
                TotalSnapshotNumber = long.MaxValue,
                StartTimestamp = DateTime.MinValue.ToUniversalTime().ToTimestamp(),
                EndTimestamp = DateTime.MaxValue.ToUniversalTime().ToTimestamp()
            };
            State.VoteContract.Register.Send(votingRegisterInput);

            State.MinerElectionVotingItemId.Value = Hash.FromTwoHashes(Hash.FromMessage(votingRegisterInput),
                Hash.FromMessage(Context.Self));

            State.VotingEventRegistered.Value = true;
            return new Empty();
        }

        public override Empty TakeSnapshot(TakeElectionSnapshotInput input)
        {
            Context.LogDebug(() => "Entered TakeSnapshot.");
            var snapshot = new TermSnapshot
            {
                MinedBlocks = input.MinedBlocks,
                EndRoundNumber = input.RoundNumber
            };
            foreach (var publicKey in State.Candidates.Value.Value)
            {
                var votes = State.CandidateVotes[publicKey.ToHex()];
                var validObtainedVotesAmount = 0L;
                if (votes != null)
                {
                    validObtainedVotesAmount = votes.ObtainedActiveVotedVotesAmount;
                }

                snapshot.ElectionResult.Add(publicKey.ToHex(), validObtainedVotesAmount);
            }

            // Update snapshot of corresponding voting record by the way.
            State.VoteContract.TakeSnapshot.Send(new TakeSnapshotInput
            {
                SnapshotNumber = input.TermNumber,
                VotingItemId = State.MinerElectionVotingItemId.Value
            });


            State.Snapshots[input.TermNumber] = snapshot;
            State.CurrentTermNumber.Value = input.TermNumber.Add(1);

            var previousMiners = State.AEDPoSContract.GetPreviousRoundInformation.Call(new Empty())
                .RealTimeMinersInformation.Keys.ToList();

            var victories = GetVictories(previousMiners);
            var previousMinersAddresses = new List<Address>();
            foreach (var publicKey in previousMiners)
            {
                var address = Address.FromPublicKey(ByteArrayHelpers.FromHexString(publicKey));

                previousMinersAddresses.Add(address);

/*                var history = State.CandidateInformationMap[publicKey];
                history.Terms.Add(input.TermNumber - 1);

                if (victories.Contains(publicKey.ToByteString()))
                {
                    history.ContinualAppointmentCount = history.ContinualAppointmentCount.Add(1);
                    reElectionProfitAddWeights.Weights.Add(new WeightMap
                    {
                        Receiver = address,
                        Weight = history.ContinualAppointmentCount
                    });
                }
                else
                {
                    history.ContinualAppointmentCount = 0;
                }

                var votes = State.CandidateVotes[publicKey];
                if (votes != null)
                {
                    votesWeightRewardProfitAddWeights.Weights.Add(new WeightMap
                    {
                        Receiver = address,
                        Weight = votes.ObtainedActiveVotedVotesAmount
                    });
                }

                State.CandidateInformationMap[publicKey] = history;*/
            }

            return new Empty();
        }

        /// <summary>
        /// Update the candidate information,if it's not evil node.
        /// </summary>
        /// <param name="input">UpdateCandidateInformationInput</param>
        /// <returns></returns>
        public override Empty UpdateCandidateInformation(UpdateCandidateInformationInput input)
        {
            var candidateInformation = State.CandidateInformationMap[input.Pubkey];

            if (input.IsEvilNode)
            {
                var publicKeyByte = ByteArrayHelpers.FromHexString(input.Pubkey);
                State.BlackList.Value.Value.Add(ByteString.CopyFrom(publicKeyByte));
                State.ProfitContract.SubWeight.Send(new SubWeightInput
                {
                    ProfitId = State.SubsidyHash.Value,
                    Receiver = Address.FromPublicKey(publicKeyByte)
                });
                Context.LogDebug(() => $"Marked {input.Pubkey.Substring(0, 10)} as an evil node.");
                // TODO: Set to null.
                State.CandidateInformationMap[input.Pubkey] = new CandidateInformation();
                var candidates = State.Candidates.Value;
                candidates.Value.Remove(ByteString.CopyFrom(publicKeyByte));
                State.Candidates.Value = candidates;
                return new Empty();
            }

            candidateInformation.ProducedBlocks = candidateInformation.ProducedBlocks.Add(input.RecentlyProducedBlocks);
            candidateInformation.MissedTimeSlots =
                candidateInformation.MissedTimeSlots.Add(input.RecentlyMissedTimeSlots);
            State.CandidateInformationMap[input.Pubkey] = candidateInformation;
            return new Empty();
        }

        public override Empty UpdateMinersCount(UpdateMinersCountInput input)
        {
            Assert(State.AEDPoSContract.Value == Context.Sender,
                "Only consensus contract can update miners count.");
            State.MinersCount.Value = input.MinersCount;
            return new Empty();
        }

        public override Empty SetTreasuryProfitIds(SetTreasuryProfitIdsInput input)
        {
            Assert(State.TreasuryHash.Value == null, "Treasury profit ids already set.");
            State.TreasuryHash.Value = input.TreasuryHash;
            State.WelfareHash.Value = input.WelfareHash;
            State.SubsidyHash.Value = input.SubsidyHash;
            return new Empty();
        }
    }
}