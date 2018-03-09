using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;
using System.Collections.Generic;
using System;
using System.ComponentModel;

namespace Neobex.Contract
{

    /**
     * Neobex is a smart marketeplace which offers cash free and smart trades.
     * Smart trades: Are seamless interdependent processing of 
     *          NBX Trades: User can Sell/Buy, Auction/Bid physical goods with crypto token NBX. 
     *          Smart Barters: Circular barters are processed to raise the probability of interest in physical goods between users. 
     *                         Remaining balance is adjusted in NBX 
     *          Hybrid trades: 
     *          
     *          
     * NBX:        Symbol of  NEP 5 Token for Neobex market.
     * Offer:      User's offer to Buy, Bid or Barter the other itmes in the market. Evantually it consists what user want and what he offers. Offer can be in physical goods, NBX tokens or both
     * NBX Offer:  An offer which is not dependent on physical goods but consist of only NBX tokens 
     * NBX Bid:    A bid which is not dependent on physical goods but consist of only NBX tokens 
     */

    public class NeobexSmartContract : SmartContract
    {
        private const int transactionFeePercentage = 1;

        #region Events

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;

        [DisplayName("refund")]
        public static event Action<byte[], BigInteger> Refund;

        [DisplayName("refundAsset")]
        public static event Action<byte[], BigInteger> RefundAsset;

        // byte[]sender, byte[] recipient, byte[] Currency, biginteger amount
        [DisplayName("transferAsset")]
        public static event Action<byte[], byte[], byte[], BigInteger> TransferAsset;

        #endregion

        #region constants

        private static readonly byte[] OWNER = "Ab8cGoJHyjp4e6iWgW9HmCZtbZ6q93VvcJ".ToScriptHash();
        private static readonly byte[] NEO = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private static readonly byte[] GAS = { 231, 45, 40, 105, 121, 238, 108, 177, 183, 230, 93, 253, 223, 178, 227, 132, 16, 11, 141, 20, 142, 119, 88, 222, 66, 228, 22, 139, 113, 121, 44, 96 };


        private static readonly byte[] FALSE = { 0 };
        private static readonly byte[] TRUE = { 1 };

        private static readonly byte[] KEY_TO_FIRST_NBX_OFFER = "FNO".AsByteArray();            // just unique initials as value
        private static readonly byte[] KEY_TO_EARLIEST_CLOSING_AUCTION = "ECA".AsByteArray();
        private static readonly byte[] KEY_TO_OFFER_ID_LINKING_ALL_VISITED = "OILAV".AsByteArray();

        private static readonly byte[] POSTFIX_FOR_VISITED_MARK_KEY = "VMK".AsByteArray();
        private static readonly byte[] POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY = "FBTAK".AsByteArray();

        private static readonly byte[] CATAGORY_SALE = "S".AsByteArray();
        private static readonly byte[] CATAGORY_AUCTION = "A".AsByteArray();
        private static readonly byte[] CATAGORY_OFFER = "O".AsByteArray();
        private static readonly byte[] MARK_VISITED = "V".AsByteArray();

        private static readonly byte[] USD = "USD".AsByteArray();
        private static readonly byte[] NBX = "NBX".AsByteArray();
        private static readonly int CURRENCY_LENGTH = USD.Length;

        private static readonly int HASH_LENGTH = 20;
        private static readonly int PADDED_NUMBER_LENGTH = 8;
        private static readonly int PADDED_DATE_TIME_LENGTH = 12;
        private static readonly int MAX_WANTED_PER_OFFER = 3;
        private static readonly int IDENTIFIER_LENGTH = CATAGORY_AUCTION.Length;
        private static readonly int MAX_PROCESSING_FEE = 5;

        private static byte[] PADDING_DATA = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // for some fixed size data storage

        //NEP5 token constants
        public static string Name() => "Neobex Coin";
        public static string Symbol() => "NBX";
        public static byte Decimals() => 8;


        // ICO constatns
        private const ulong FACTOR = 1000000;
        private const ulong MAX_SUPPLY = 500000 * FACTOR;                     // total supply of the NBX tokens
        private const ulong ICO_BASE_EXCHANGE_RATE = 1000 * FACTOR;          // number of tokens offered per NEO

        private const int ICO_DURATION = 45;                                 // number of days to run the ICO
        private const int ICO_DURATION_SECONDS = ICO_DURATION * 86400;
        private const int ICO_START_TIME_STAMP = 1520208000;                 //5-March-2018  // for testing only // ICO on mainnet later according to roadmap
        private const int ICO_END_TIMESTAMP = ICO_START_TIME_STAMP + ICO_DURATION_SECONDS;
        private static ulong NBX_TO_USD = 5; // NBX price is 2 USD in this case. change able and applied latest in processing.

        #endregion


        /// <summary>
        /// Main method of a contract
        /// </summary>
        /// <param name="operation">Method to invoke</param>
        /// <param name="args">Method parameters</param>
        /// <returns>Method's return value or false if operation is invalid</returns>
        public static object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Application)
            {

                if (operation.Equals("Sell") && args.Length == 6)
                {  // call to list what user is offering for direct sale. he is not interested in barter.
                    return AddSale((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[4], (BigInteger)args[5]);
                }
                if (operation.Equals("Auction") && args.Length == 6)
                {   // call to list what user is offering for Auction. // no barter for this user
                    return AddAuction((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[4], (BigInteger)args[5]);
                }
                if (operation.Equals("buyOrBarter") && args.Length == 7)
                {   // call to list what user is offering and what he want in exchange.
                    // User can offer physical goods, nbx tokens or both to sum up total worth. 
                    // User can mention what he want(param 7, index 6) in exchange or can edit later. 
                    // User can want in exchange items being sold or exchanged by other users.
                    // All the buyings and exchanges will go through hybrid of circular barters and NBX trades.

                    return AddOffer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (BigInteger)args[3], (BigInteger)args[4], (byte[])args[5], (byte[][])args[6], false);
                }

                if (operation.Equals("bid") && args.Length == 6)
                {   // call to add a bid to some already listed auction. Bids in Nbx. no barter or exchange of goods
                    return MakeBid((byte[])args[0], (byte[])args[1], (BigInteger)args[3], (byte[])args[4], (byte[])args[5]);
                }

                if (operation.Equals("addWantedIdsToOffer") && args.Length == 2)
                {   // call to Add Ids of other offers which the user want in exchange of his own offer. Wanted id can be id of offer, sale or auction
                    return AddWantedIdsToOffer((byte[])args[0], (byte[][])args[1]);
                }
                if (operation.Equals("removeWantedIdsFromOffer") && args.Length == 2)
                {   // call to remove Ids of other offers which the user wanted in exchange of his own offer
                    return RemoveWantedIdsFromOffer((byte[])args[0], (byte[][])args[1]);
                }
                if (operation.Equals("updateUsdToNbxRate") && args.Length == 1)
                {   // TODO to be implemented 
                    //hard coded fix rate for now.
                }
                if (operation.Equals("cancelOffer") && args.Length == 1)
                {
                    return CancelOffer((byte[])args[0]);
                }
                if (operation.Equals("cancelBid") && args.Length == 1)
                {
                    return CancelBid((byte[])args[0]);
                }
                if (operation.Equals("cancelAuction") && args.Length == 1)
                {
                    return DeleteAuctionAndItsBids((byte[])args[0]);
                }
                if (operation.Equals("cancelSale") && args.Length == 1)
                {
                    return CancelSale((byte[])args[0]);
                }
                if (operation.Equals("balanceOf") && args.Length == 1)
                {
                    return BalanceOf((byte[])args[0]);
                }
                if (operation.Equals("transfer") && args.Length == 3)
                {
                    return Transfer((byte[])args[0], (byte[])args[1], (BigInteger)args[2]); // from, to , value
                }
                if (operation.Equals("decimals") && args.Length == 0)
                {
                    return Decimals();
                }
                if (operation.Equals("mintTokens") && args.Length == 0)
                {
                    return MintTokens();
                }
                if (operation.Equals("name") && args.Length == 0)
                {
                    return Name();
                }
                if (operation.Equals("symbol") && args.Length == 0)
                {
                    return Symbol();
                }
                if (operation.Equals("totalSupply") && args.Length == 0)
                {
                    return TotalSupply();
                }
                if (operation.Equals("initSC") && args.Length == 0)
                {
                    return InitSC();
                }

                return false;

            }
            else if (Runtime.Trigger == TriggerType.Verification)
            {
                // contract has received a ContractTransaction
                Runtime.Notify("Main() TriggerType.Verification", TriggerType.Verification);

                // todo: implement balance check
                return VerifyOwnerAccount();
            }
            return false;
        }

        private static ulong UpdatedNbxToUsdRate()
        {
            return 2;// to be implemented
        }

        class ClosingCircle
        {
            public byte[] terminalOfferId;
            public Chain candidateChain;
            public int loopSize;
        }

        #region processing
        private static ClosingCircle closingCircle = new ClosingCircle();

        /**
         * This method is main processing engine / controller / smart strategy as mentioned in documents/white paperr
         * it delegates the calls to approperiate methods to perform complex trades seamlessly
         */
        private static void ProcessTrades(Offer rootOffer)
        {
            closingCircle.loopSize = 0;

            if (!IsProcessingInProgress())// acquireLock
            {
                Chain chain = new Chain();
                chain.offerId = rootOffer.Id;

                LookupTradeOpportunities(rootOffer, chain);

                if (closingCircle.loopSize > 0)
                {
                    PerformCircularTrades();
                }

                ClearProcessingData();
            }
        }

        private static void LookupTradeOpportunities(Offer offer, Chain chain)
        {
            MarkVisited(offer.Id);
            if (offer.WantedIds != null)
            {
                if (!AlreadyVisited(offer.Id))
                {
                    foreach (byte[] wantedId in offer.WantedIds)
                    {
                        if (wantedId != null)
                        {
                            byte[] catagory = wantedId.Range(0, IDENTIFIER_LENGTH);


                            if (catagory.Equals(CATAGORY_OFFER))
                            {

                                Offer wantedNode = GetOfferFromStorage(wantedId);

                                if (wantedNode == null)
                                {
                                    byte[][] toBeDeleted = new byte[1][] { wantedId };
                                    RemoveWantedIdsFromOffer(offer.Id, toBeDeleted);
                                }

                                else if (IsOfferReadyForTrade(offer, wantedNode))
                                {
                                    LinkOfferInTheTradeCircle(chain, wantedNode);
                                }
                            }
                            else if (catagory.Equals(CATAGORY_SALE) || catagory.Equals(CATAGORY_AUCTION))
                            {
                                // incase of auction: continue only if this is heighest bid or at least heighest nbx bid(with suffficient balance) for a closed auction
                                if (catagory.Equals(CATAGORY_AUCTION) && !IsHeigestValidBid(wantedId, offer.Id))
                                {
                                    continue;
                                }

                                chain = LinkSaleOrAutionInTheTradeCircle(chain, wantedId, catagory);
                                byte[] nbxOfferId = Get(KEY_TO_FIRST_NBX_OFFER);
                                while (nbxOfferId != null)
                                {
                                    Offer nbxOffer = GetOfferFromStorage(nbxOfferId);
                                    {
                                        LinkOfferInTheTradeCircle(chain, nbxOffer);
                                    }
                                    nbxOfferId = nbxOffer.NextNbxOffer;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void LinkOfferInTheTradeCircle(Chain chain, Offer nbxOffer)
        {
            Chain childChain = new Chain();

            childChain.offerId = nbxOffer.Id;
            childChain.parentChain = chain;
            childChain.Catagory = "offer".AsByteArray();
            FindTheBiggestTradeCircle(nbxOffer, childChain.parentChain);
            LookupTradeOpportunities(nbxOffer, chain); // recurseive call through the graph to find the bigest trade opportunity
        }

        private static Chain LinkSaleOrAutionInTheTradeCircle(Chain chain, byte[] id, byte[] catagory)
        {
            Chain childChain = new Chain();

            childChain.offerId = id;
            childChain.parentChain = chain;
            childChain.Catagory = catagory;

            return chain;
        }

        private static bool IsOfferReadyForTrade(Offer offer, Offer wantedNode)
        {
            return (offer.ItemsNetWorth > wantedNode.ItemsNetWorth) || BalanceOf(offer.WalletAddress) > AmountAfterProcessingFee(wantedNode.ItemsNetWorth - offer.ItemsNetWorth);
        }

        private static void FindTheBiggestTradeCircle(Offer node, Chain chain)
        {
            int distance = 0;
            byte[] terminalOfferId = null;
            do
            {
                foreach (byte[] id in node.WantedIds)
                {
                    if (id.Equals(chain.offerId))
                    {
                        terminalOfferId = chain.offerId;
                    }
                }


                chain = chain.parentChain;
                distance++;
            } while (chain != null);// keep going upwards to find closing node as far as possible

            if (terminalOfferId != null && closingCircle.loopSize < distance && IsCircleReadyForTrade(chain, terminalOfferId))
            {
                closingCircle.candidateChain = chain;
                closingCircle.terminalOfferId = terminalOfferId;
                closingCircle.loopSize = distance;
            }
        }

        private static bool IsCircleReadyForTrade(Chain chain, byte[] terminalOfferId)
        {
            uint currentTimestamp = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

            while (!chain.offerId.Equals(terminalOfferId))
            {
                Offer wanted = GetOfferFromStorage(chain.offerId);
                Offer offer = GetOfferFromStorage(chain.parentChain.offerId);

                if (offer.IsBid && (GetAuctionFromStorage(offer.WantedIds[0]).ClosingAt > currentTimestamp || IsHeigestValidBid(offer.WantedIds[0], offer.Id)))// auction is not closed or bid is not heigest
                {
                    return false;
                }

                chain = chain.parentChain;
            }

            return true;
        }

        private static void PerformCircularTrades()
        {

            // As balance of all the users involved is already checked accounts are locked untill end of process
            // Thus we can use admin account to settle the circular transfers of NBX balance. Admin account will have some subtractions and additions but finally it will have no change in balance end of process
            Chain chain = closingCircle.candidateChain;
            while (!chain.offerId.Equals(closingCircle.terminalOfferId))
            {
                Offer wanted = GetOfferFromStorage(chain.offerId);
                Offer offer = GetOfferFromStorage(chain.parentChain.offerId);
                Runtime.Notify(TradeNotificationMessage(wanted, offer));

                BigInteger offerAmountGap = offer.ItemsNetWorth - wanted.ItemsNetWorth;

                if (offerAmountGap > 0)
                {
                    Transfer(offer.WalletAddress, OWNER, offerAmountGap);
                }
                else if (offerAmountGap < 0)
                {
                    offerAmountGap = AmountAfterProcessingFee(offerAmountGap);
                    Transfer(OWNER, offer.WalletAddress, 0 - offerAmountGap); // admin can go minus but evantualy will in profit end of circular processing and fee charges
                }
                chain = chain.parentChain;
            }
        }

        private static bool IsHeigestValidBid(byte[] auctionId, byte[] bidId)
        {
            Auction auction = GetAuctionFromStorage(auctionId);
            uint currentTimestamp = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

            if (auction.ClosingAt < currentTimestamp)// auction is not closed yet
            {
                return false;
            }

            Offer bid = GetOfferFromStorage(bidId);
            if (BalanceOf(bid.WalletAddress) < AmountAfterProcessingFee((bid.TotalAmount - bid.ItemsNetWorth)))
            {

                // auction closed but this bidder don't have suficient balance. should be disqualified so that others can get chance
                CancelBid(bid);
                return false;
            }

            byte[] higestBidId = Get(POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY);
            Offer heighestBid = GetOfferFromStorage(higestBidId);

            // return true if the bid is the heigest amount 
            //or those heigest to it are not processable due to balance 
            //or those heigest to it are dependent on other barter circles. this bid will be prioritized for being more liquid/processable
            while (heighestBid.TotalAmount > bid.TotalAmount)
            { //prioratize the bids heiger to the comparing one

                if (heighestBid.ItemsNetWorth == 0 && BalanceOf(heighestBid.WalletAddress) >= AmountAfterProcessingFee(heighestBid.TotalAmount - heighestBid.ItemsNetWorth)) // heigest bid is nbx bid and bidder have sufficient balance
                {
                    return heighestBid.Id.Equals(bidId);

                }
                heighestBid = GetOfferFromStorage(heighestBid.NextBid);//Next bid smaller to the heighest but heighest to rest

            }

            return false;

        }

        private static BigInteger AmountAfterProcessingFee(BigInteger amount)
        {
            BigInteger processingFee = (amount / 100) * transactionFeePercentage;
            amount += processingFee;

            if (processingFee > MAX_PROCESSING_FEE)
            {
                processingFee = MAX_PROCESSING_FEE;
            }
            return amount;
        }

        private static string TradeNotificationMessage(Offer wanted, Offer offer)
        {
            string message;
            if (offer.IsBid)
            {
                message = "Bid ";
            }
            else
            {
                message = "Offer ";
            }
            return message + " for " + wanted.Id + " is accepted. Collection/Delivery  due.";
        }

        public static void ProcessAllAuctions()
        {

            uint currentTimestamp = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;

            byte[] auctionId = Get(KEY_TO_EARLIEST_CLOSING_AUCTION);

            while (auctionId != null)
            {
                Auction auction = GetAuctionFromStorage(auctionId);

                if (auction.ClosingAt > currentTimestamp)
                {
                    return;
                }
                ProcessAuctionForHeighestBid(auction);
                DeleteAuctionAndItsBids(auctionId);
            }
        }

        private static void ProcessAuctionForHeighestBid(Auction auction)
        {
            byte[] bidId = Get(auction.Id.Concat(POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY));


            Offer bid = GetOfferFromStorage(bidId);
            while (bid != null && auction != null && bid.TotalAmount > auction.MinAmount)
            {
                if (BalanceOf(bid.WalletAddress) >= AmountAfterProcessingFee(bid.TotalAmount - bid.ItemsNetWorth))
                {
                    ProcessTrades(bid);
                }
                bid = GetOfferFromStorage(bid.NextBid);
                auction = GetAuctionFromStorage(auction.Id); // will be null if some bid was traded successfully
            }

            DeleteAuctionAndItsBids(auction.Id);// after going through all the bids, successful or not. bid will be closed. 
        }

        private static bool IsProcessingInProgress()
        {
            byte[] offerId = Get(KEY_TO_OFFER_ID_LINKING_ALL_VISITED);
            return offerId != null;
        }

        private static bool AlreadyVisited(byte[] offerId)
        {
            byte[] key = offerId.Concat(POSTFIX_FOR_VISITED_MARK_KEY);
            byte[] data = Get(key);
            return data != null && data.Length > 0;

        }

        private static void MarkVisited(byte[] offerId)
        {
            var rootOfferVisited = Get(KEY_TO_OFFER_ID_LINKING_ALL_VISITED);

            byte[] keyForVisitMark = offerId.Concat(POSTFIX_FOR_VISITED_MARK_KEY);
            var visitMarkAndLinkToNext = MARK_VISITED;

            if (rootOfferVisited != null)
            {
                visitMarkAndLinkToNext = visitMarkAndLinkToNext.Concat(rootOfferVisited);
            }

            Put(keyForVisitMark, visitMarkAndLinkToNext);
            Put(KEY_TO_OFFER_ID_LINKING_ALL_VISITED, offerId);

        }

        private static void ClearProcessingData()
        {
            byte[] offerId = Get(KEY_TO_OFFER_ID_LINKING_ALL_VISITED);
            while (offerId != null)
            {
                byte[] key = offerId.Concat(POSTFIX_FOR_VISITED_MARK_KEY);
                byte[] data = Get(key);

                Delete(key);

                if (data != null && data.Length > MARK_VISITED.Length)
                {
                    offerId = data.Range(MARK_VISITED.Length, HASH_LENGTH);
                }
                else
                {
                    offerId = null;
                }
            }
            Delete(KEY_TO_OFFER_ID_LINKING_ALL_VISITED);
        }

        private static BigInteger AmountInNBX(BigInteger amount, byte[] currency, BigInteger nbxToUsdRate)
        {

            if (currency.Equals(USD))
            {
                return (BigInteger)(amount / nbxToUsdRate);
            }


            return amount;
        }
        #endregion

        #region data management and persistance
        private static bool AddOffer(byte[] id, byte[] userId, BigInteger validTill, BigInteger itemsNetWorth, BigInteger totalAmount, byte[] currency, byte[][] wantedIds, bool isBid)
        {
            Offer offer = new Offer(id, userId, validTill, itemsNetWorth, totalAmount, currency, wantedIds, true);
            PutOfferToStorage(offer, isBid);
            return true;
        }

        private static bool AddWantedIdsToOffer(byte[] id, byte[][] newWantedIds)
        {

            Offer offer = GetOfferFromStorage(id);

            byte[][] existingWantedIds = new byte[MAX_WANTED_PER_OFFER][];

            int existingCount = 2;

            for (int i = 0; i < newWantedIds.Length && newWantedIds[i] != null; i++)
            {
                existingWantedIds[(i + existingCount) % 3] = (byte[])newWantedIds[i];
            }

            offer.WantedIds = existingWantedIds;
            PutOfferToStorage(offer, false);

            return true;
        }

        private static bool RemoveWantedIdsFromOffer(byte[] id, byte[][] wantedIds)
        {
            Offer offer = GetOfferFromStorage(id);
            byte[][] existingWantedIds = new byte[MAX_WANTED_PER_OFFER][];

            for (int i = 0; i < existingWantedIds.Length; i++)
            {
                for (int j = 0; j < wantedIds.Length; j++)
                {
                    if (existingWantedIds[i].Equals(wantedIds[j]))
                    {
                        existingWantedIds[i] = null;
                    }
                }
            }

            offer.WantedIds = existingWantedIds;
            PutOfferToStorage(offer, false);

            return true;
        }

        private static bool PutOfferToStorage(Offer offer, bool linkTheBidInList)
        {

            byte[] bitmap;
            if (offer.IsBid) { bitmap = TRUE; } else { bitmap = FALSE; }

            bool nextBidPresent = false;
            bool nextNbxOfferPresent = false;

            var offerData = offer.WalletAddress;
            if (offer.ValidTill != null)
            {
                offerData.Concat(offer.ValidTill.AsByteArray().Concat(PADDING_DATA).Take(PADDED_DATE_TIME_LENGTH));
            }

            offerData.Concat(offer.ItemsNetWorth.AsByteArray().Concat(PADDING_DATA).Take(PADDED_NUMBER_LENGTH))
                                      .Concat(offer.TotalAmount.AsByteArray().Concat(PADDING_DATA).Take(PADDED_NUMBER_LENGTH))
                                      .Concat(offer.Currency);

            if (offer.WantedIds != null)
            {
                foreach (byte[] id in offer.WantedIds)
                {
                    offerData = offerData.Concat(id);
                }
            }

            if (offer.IsBid)
            {
                if (offer.NextBid != null)
                {
                    offerData = offerData.Concat(offer.NextBid);
                    nextBidPresent = true;
                }

                if (linkTheBidInList) // link the build in link list in decending order. so that higest valid bid can be picked first.
                {
                    byte[] aucitonId = offer.WantedIds[0];
                    Offer bidOnLeft = null;
                    Offer bidOnRight = GetOfferFromStorage(Get(aucitonId.Concat(POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY)));

                    if (bidOnRight == null)
                    {
                        Put(aucitonId.Concat(POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY), offer.Id);
                    }
                    else
                    {
                        while (bidOnRight != null && bidOnRight.TotalAmount > offer.TotalAmount) // link in decending order
                        {
                            bidOnLeft = bidOnRight;
                            bidOnRight = GetOfferFromStorage(bidOnRight.NextBid);

                        }
                        if (bidOnLeft != null)
                        {

                            bidOnLeft.NextBid = offer.Id;
                        }
                        if (bidOnRight != null)
                        {
                            offer.NextBid = bidOnRight.Id;
                        }

                    }
                }

            }

            if (offer.TotalAmount > 0 && offer.ItemsNetWorth == 0)
            {
                byte[] nextNbxOfferId = Get(KEY_TO_FIRST_NBX_OFFER);
                Put(KEY_TO_FIRST_NBX_OFFER, offer.Id);
                if (nextNbxOfferId != null)
                {
                    nextNbxOfferPresent = true;
                    offerData = offerData.Concat(nextNbxOfferId);
                }
            }

            if (nextBidPresent)
            {
                bitmap = bitmap.Concat(TRUE);
            }
            else
            {
                bitmap = bitmap.Concat(FALSE);
            }

            if (nextNbxOfferPresent)
            {
                bitmap = bitmap.Concat(TRUE);
            }
            else
            {
                bitmap = bitmap.Concat(FALSE);
            }

            if (offer.ItemsNetWorth > 0)
            {
                bitmap = bitmap.Concat(TRUE);
            }
            else
            {
                bitmap = bitmap.Concat(FALSE);
            }

            if (offer.ValidTill != null)
            {
                bitmap = bitmap.Concat(TRUE);
            }
            else
            {
                bitmap = bitmap.Concat(FALSE);
            }

            Put(CATAGORY_OFFER.Concat(offer.Id), offerData);
            return true;
        }

        private static Offer GetOfferFromStorage(byte[] id)
        {
            var offerData = Get(CATAGORY_OFFER.Concat(id));

            if (offerData == null)
            {
                return null;
            }

            Offer offer = new Offer();

            offer.Id = id;
            int index = 0;

            offer.IsBid = offerData.Range(index, 1).Equals(TRUE);
            index += 1;

            bool nextBidPresent = offerData.Range(index, 1).Equals(TRUE);
            index += 1;

            bool nextNbxOfferPresent = offerData.Range(index, 1).Equals(TRUE);
            index += 1;

            bool itemsValuePresent = offerData.Range(index, 1).Equals(TRUE);
            index += 1;

            bool validityDatePresent = offerData.Range(index, 1).Equals(TRUE);
            index += 1;

            offer.WalletAddress = offerData.Range(index, HASH_LENGTH);
            index += HASH_LENGTH;

            if (validityDatePresent)
            {
                offer.ValidTill = offerData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
                index += PADDED_NUMBER_LENGTH;
            }

            if (itemsValuePresent)
            {
                offer.ItemsNetWorth = offerData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
                index += PADDED_NUMBER_LENGTH;
            }
            offer.TotalAmount = offerData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
            index += PADDED_NUMBER_LENGTH;

            offer.Currency = offerData.Range(index, CURRENCY_LENGTH);
            index += CURRENCY_LENGTH;

            if (nextBidPresent)
            {
                offer.NextBid = offerData.Range(index, HASH_LENGTH);
                index += HASH_LENGTH;
            }
            if (nextNbxOfferPresent)
            {
                offer.NextNbxOffer = offerData.Range(index, HASH_LENGTH);
                index += HASH_LENGTH;
            }

            var wantedIdsData = offerData.Range(index, offerData.Length - (index + 1));
            offer.WantedIds = new byte[MAX_WANTED_PER_OFFER][];

            for (int wIndex = 0; wIndex <= wantedIdsData.Length; wIndex += HASH_LENGTH)
            {
                offer.WantedIds[wIndex] = wantedIdsData.Range(wIndex, HASH_LENGTH);
            }

            if (offer.Currency.Equals(USD))
            {
                offer.TotalAmount = offer.TotalAmount / NBX_TO_USD;
                offer.ItemsNetWorth = offer.ItemsNetWorth / NBX_TO_USD;
            }



            return offer;
        }

        private static bool AddSale(byte[] id, byte[] userId, BigInteger amount, byte[] currency, BigInteger validTill)
        {
            Sale sale = new Sale(id, userId, amount, currency, validTill);
            return PutSaleToStorge(sale);


        }

        private static bool PutSaleToStorge(Sale sale)
        {
            var saleData = sale.WalletAddress
                .Concat(sale.Amount.AsByteArray()
                .Concat(PADDING_DATA).Take(PADDED_NUMBER_LENGTH))
                .Concat(sale.Currency)
                .Concat(sale.ValidTill.AsByteArray()
                .Concat(PADDING_DATA).Take(PADDED_NUMBER_LENGTH));

            if (sale.Buyer != null)
            {
                saleData = saleData.Concat(sale.Buyer);
            }

            Put(CATAGORY_SALE.Concat(sale.Id), saleData);
            return true;
        }

        private static Sale GetSaleFromStorage(byte[] id)
        {
            var saleData = Get(CATAGORY_SALE.Concat(id));
            if (saleData == null)
            {
                return null;
            }

            Sale sale = new Sale();

            sale.Id = id;
            int index = 0;

            sale.WalletAddress = saleData.Range(index, HASH_LENGTH);
            index += HASH_LENGTH;

            sale.ValidTill = saleData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
            index += PADDED_NUMBER_LENGTH;


            sale.Amount = saleData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
            index += PADDED_NUMBER_LENGTH;

            sale.Currency = saleData.Range(index, CURRENCY_LENGTH);
            index += CURRENCY_LENGTH;

            if (saleData.Length > HASH_LENGTH)
            {
                sale.Buyer = saleData.Range(index, HASH_LENGTH);
            }

            if (sale.Currency.Equals(USD))
            {
                sale.Amount = sale.Amount / NBX_TO_USD;
            }

            return sale;
        }

        private static bool AddAuction(byte[] id, byte[] userId, BigInteger minAmount, byte[] currency, BigInteger closing)
        {
            Auction newAuction = new Auction(id, userId, minAmount, currency, closing);
            return LinkAuctionInAscendingOrderInTime(newAuction);
        }

        private static bool LinkAuctionInAscendingOrderInTime(Auction newAuction)
        {
            Auction auctionOnLeft = null;
            Auction auctionOnRight = GetAuctionFromStorage(KEY_TO_EARLIEST_CLOSING_AUCTION);

            if (auctionOnRight == null)
            {
                Put(KEY_TO_EARLIEST_CLOSING_AUCTION, newAuction.Id);
            }
            else
            {
                while (auctionOnRight.ClosingAt < newAuction.ClosingAt && auctionOnRight.Next != null)
                {
                    auctionOnLeft = auctionOnRight;
                    auctionOnRight = GetAuctionFromStorage(auctionOnRight.Next);
                }
                if (auctionOnLeft != null)
                {
                    auctionOnLeft.Next = newAuction.Id;
                    PutAuctionToStorage(auctionOnLeft);
                }

                newAuction.Next = auctionOnRight.Id;
                PutAuctionToStorage(auctionOnRight);
            }

            PutAuctionToStorage(newAuction);
            return true;
        }

        private static void PutAuctionToStorage(Auction newAuction)
        {
            byte[] bitmap = new byte[] { 0, 0 };

            var auctionData = newAuction.WalletAddress
                                    .Concat(newAuction.MinAmount.AsByteArray().Concat(PADDING_DATA).Take(PADDED_NUMBER_LENGTH))
                                    .Concat(newAuction.Currency)
                                    .Concat(newAuction.ClosingAt.AsByteArray().Concat(PADDING_DATA).Take(PADDED_NUMBER_LENGTH));

            if (newAuction.Winner != null)
            {
                bitmap[0] = 1;
                auctionData = auctionData.Concat(newAuction.Winner);
            }

            if (newAuction.Next != null)
            {
                bitmap[1] = 1;

                auctionData = auctionData.Concat(newAuction.Next);
            }

            auctionData = bitmap.Concat(auctionData);

            Put(CATAGORY_AUCTION.Concat(newAuction.Id), auctionData);
        }

        private static Auction GetAuctionFromStorage(byte[] id)
        {
            var auctionData = Get(CATAGORY_AUCTION.Concat(id));

            Auction auction = new Auction();

            auction.Id = id;
            int index = 0;

            var bitmap = auctionData.Range(index, 2);
            index += 2;

            auction.WalletAddress = auctionData.Range(index, HASH_LENGTH);
            index += HASH_LENGTH;

            auction.ClosingAt = auctionData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
            index += PADDED_NUMBER_LENGTH;

            auction.MinAmount = auctionData.Range(index, PADDED_NUMBER_LENGTH).AsBigInteger();
            index += PADDED_NUMBER_LENGTH;

            auction.Currency = auctionData.Range(index, CURRENCY_LENGTH);
            index += CURRENCY_LENGTH;

            if (bitmap[0] == 1)
            {
                auction.Winner = auctionData.Range(index, HASH_LENGTH);
                index += HASH_LENGTH;
            }

            if (bitmap[1] == 1)
            {
                auction.Next = auctionData.Range(index, HASH_LENGTH);
                index += HASH_LENGTH;
            }


            if (auction.Currency.Equals(USD))
            {
                auction.MinAmount = auction.MinAmount / NBX_TO_USD;
            }
            return auction;
        }

        private static object MakeBid(byte[] id, byte[] userId, BigInteger totalAmount, byte[] currency, byte[] auctionId)
        {
            byte[][] wantedIds = new byte[1][] { auctionId }; // auction id is evantualy wanted id
            return AddOffer(id, userId, 0, 0, totalAmount, currency, wantedIds, true);// a bid is stored as an offer
        }

        private static bool CancelSale(byte[] saleId)
        {
            Delete(saleId); // straight delete from storage, as a sale has no related links to be delted. 
            return true;
        }

        private static bool CancelBid(byte[] bidId)
        {
            Offer bidToDelete = GetOfferFromStorage(bidId);

            if (bidToDelete == null)
            {
                return true;
            }
            return CancelBid(bidToDelete);
        }

        /**
        * Before deleting a Bid, its links from the link list are removed. As bids are stored in list in Descending order for fast heigest bid 
        * Id of the first bid in the list is updated if required.
        */
        private static bool CancelBid(Offer bidToDelete)
        {
            Offer bidOnLeft = null;

            byte[] currentBidId = Get(bidToDelete.WantedIds[0].Concat(POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY));

            while (!currentBidId.Equals(bidToDelete.Id))
            {
                bidOnLeft = GetOfferFromStorage(currentBidId);
                currentBidId = bidOnLeft.NextBid;
            }

            if (bidOnLeft != null)
            {
                bidOnLeft.NextBid = bidToDelete.NextBid;
                PutOfferToStorage(bidOnLeft, true);
            }
            else if (bidToDelete.NextBid != null)
            {
                // deleting Bid was at front of link list. bring next to front.
                Put(KEY_TO_EARLIEST_CLOSING_AUCTION, bidToDelete.NextBid);
            }
            else
            {   //no bid offer left in list
                Delete(KEY_TO_EARLIEST_CLOSING_AUCTION);
            }

            Delete(bidToDelete.Id);
            return true;
        }

        /**
         *Delete a bid, Nbx offer or a simple offer from Storage
         * Incase of nbx offer or Bid, links should be updated in the link list 
         */
        private static bool CancelOffer(byte[] offerId)
        {
            Offer offer = GetOfferFromStorage(offerId);
            if (offer.IsBid)
            {
                return CancelBid(offer.Id);
            }
            else if (offer.TotalAmount > 0 && offer.ItemsNetWorth == 0)
            {
                return CancelNbxOffer(offerId);
            }
            else
            {

                Delete(offerId);
                return true;
            }

        }

        /**
 * Before deleting an NBX offer, its links from the link list are removed. 
 * Id of the first NBX offer in the list is updated if required. 
 */
        private static bool CancelNbxOffer(byte[] offerId)
        {
            Offer offer = GetOfferFromStorage(offerId);

            if (offer == null)
            {
                return true;
            }

            Offer prev = null;

            byte[] currentNbxOfferId = Get(KEY_TO_FIRST_NBX_OFFER);

            while (!currentNbxOfferId.Equals(offer.Id))
            {
                prev = GetOfferFromStorage(currentNbxOfferId);
                currentNbxOfferId = prev.NextBid;
            }

            if (prev != null)
            {
                prev.NextBid = offer.NextBid;
                PutOfferToStorage(prev, true);
            }
            else if (offer.NextBid != null)
            {
                // deleting NBX offer was at front of link list. bring next to front.
                Put(KEY_TO_EARLIEST_CLOSING_AUCTION, offer.NextBid);
            }
            else
            {
                // no NBX offer left in list
                Delete(KEY_TO_EARLIEST_CLOSING_AUCTION);
            }

            Delete(offerId);
            return true;
        }

        private static bool DeleteAuctionAndItsBids(byte[] auctionId)
        {
            Auction aucitonToDelete = GetAuctionFromStorage(auctionId);

            if (aucitonToDelete == null)
            {
                return true;
            }

            Auction next = GetAuctionFromStorage(aucitonToDelete.Next);

            Auction auctionOnLeft = null;

            byte[] currentAuctionId = Get(KEY_TO_EARLIEST_CLOSING_AUCTION);

            while (!currentAuctionId.Equals(aucitonToDelete.Id))
            {
                auctionOnLeft = GetAuctionFromStorage(currentAuctionId);
                currentAuctionId = auctionOnLeft.Next;
            }

            if (auctionOnLeft != null)
            {
                auctionOnLeft.Next = aucitonToDelete.Next;
                PutAuctionToStorage(auctionOnLeft);
            }
            else if (aucitonToDelete.Next != null)
            {

                Put(KEY_TO_EARLIEST_CLOSING_AUCTION, aucitonToDelete.Next);
            }
            else
            {
                Delete(KEY_TO_EARLIEST_CLOSING_AUCTION);

            }

            Delete(auctionId);

            DeleteBidsToAuction(aucitonToDelete.Id);
            return true;
        }

        private static void DeleteBidsToAuction(byte[] aucitonId)
        {

            byte[] bidId = Get(aucitonId.Concat(POSTFIX_FOR_FIRST_BID_TO_AUCTION_KEY));


            while (bidId != null)
            {
                Offer bid = GetOfferFromStorage(bidId);
                byte[] nextBidId = bid.NextBid;
                Delete(bidId);
                bidId = nextBidId;
            }

        }

        public static bool VerifyOwnerAccount()
        {
            Runtime.Notify("VerifyOwnerAccount() Owner", GetAdminAccount());
            return VerifyWitness(GetAdminAccount());
        }

        #endregion

        /**
      * verify that the witness (invocator) is valid
      * <param name="verifiedAddress">known good address to compare with invocator</param>
      * <returns>true if account was verified</returns>
      */
        public static bool VerifyWitness(byte[] verifiedAddress)
        {
            bool isWitness = Runtime.CheckWitness(verifiedAddress);

            Runtime.Notify("VerifyWitness() verifiedAddress", verifiedAddress);
            Runtime.Notify("VerifyWitness() isWitness", isWitness);

            return isWitness;
        }

        /**
         * retrieve the script hash for the contract admin account
         * <returns>scriptHash of admin account</returns>
         */
        public static byte[] GetAdminAccount()
        {
            return OWNER;
        }

        /**
        * post deployment initialisation
        * can only be run once by contract owner
        * <returns>true if init was performed</returns>
        */
        public static bool InitSC()
        {
            if (!VerifyOwnerAccount())
            {
                // owner authentication failed
                Runtime.Log("InitSC() VerifyOwnerAccount failed");
                return false;
            }

            BigInteger totalSupply = TotalSupply();
            if (totalSupply > 0)
            {
                // contract has already been initialised
                Runtime.Log("InitSC() SC has already been initialised");
                return false;
            }

            // set txHelper to be admin account 
            //SetTXHelper(GetAdminAccount());

            Runtime.Notify("InitSC() Creating Admin Token", 1);
            ulong deployAmount = 1 * FACTOR;
            SetBalanceOf(GetAdminAccount(), deployAmount);
            SetTotalSupply(deployAmount);
            Transferred(null, GetAdminAccount(), deployAmount);
            return true;
        }

        #region NEP 5

        //////////////////////////////////////////////////////////////////////////////////////////
        // BEGIN NEP5 implementation
        //////////////////////////////////////////////////////////////////////////////////////////
        /**
         * create tokens upon receipt of neo
         */
        public static bool MintTokens()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput reference = tx.GetReferences()[0];
            if (reference.AssetId != NEO)
            {
                // transferred asset is not neo, do nothing
                Runtime.Notify("MintTokens() reference.AssetID is not NEO", reference.AssetId);
                return false;
            }

            byte[] sender = reference.ScriptHash;
            TransactionOutput[] outputs = tx.GetOutputs();
            byte[] receiver = ExecutionEngine.ExecutingScriptHash;
            ulong receivedNEO = 0;
            Runtime.Notify("DepositAsset() recipient of funds", ExecutionEngine.ExecutingScriptHash);

            // Gets the total amount of Neo transferred to the smart contract address
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == receiver)
                {
                    receivedNEO += (ulong)output.Value;
                }
            }

            Runtime.Notify("MintTokens() receivedNEO", receivedNEO);

            if (receivedNEO <= 0)
            {
                Runtime.Log("MintTokens() receivedNEO was <= 0");
                return false;
            }

            ulong exchangeRate = IcoExchangeRate();
            Runtime.Notify("MintTokens() exchangeRate", exchangeRate);

            if (exchangeRate == 0)
            {
                // ico has ended, or the token supply is exhausted
                Refund(sender, receivedNEO);
                Runtime.Log("MintTokens() exchangeRate was == 0");

                return false;
            }

            ulong numMintedTokens = receivedNEO * exchangeRate / 100000000;

            Runtime.Notify("MintTokens() receivedNEO", receivedNEO);
            Runtime.Notify("MintTokens() numMintedTokens", numMintedTokens);

            SetBalanceOf(sender, BalanceOf(sender) + numMintedTokens);
            SetTotalSupply(numMintedTokens);
            Transferred(null, sender, numMintedTokens);
            return true;
        }

        /**
         * set the total supply value
         */
        private static void SetTotalSupply(ulong newlyMintedTokens)
        {
            BigInteger currentTotalSupply = TotalSupply();
            Runtime.Notify("SetTotalSupply() newlyMintedTokens", newlyMintedTokens);
            Runtime.Notify("SetTotalSupply() currentTotalSupply", currentTotalSupply);
            Runtime.Notify("SetTotalSupply() newlyMintedTokens + currentTotalSupply", newlyMintedTokens + currentTotalSupply);

            Put("totalSupply".AsByteArray(), currentTotalSupply + newlyMintedTokens);
        }

        /**
         * how many tokens have been issued
         */
        public static BigInteger TotalSupply()
        {
            return GetAsBigInteger("totalSupply".AsByteArray());
        }

        /**
         * transfer value between from and to accounts
         */
        public static bool Transfer(byte[] from, byte[] to, BigInteger transferValue)
        {
            if (IsProcessingInProgress() && isInvolvedInLockedTradeProcessing(from))
            {

                // can not releaase funds as trades are bing processed on user accound
                Runtime.Notify("Transfer() trades in progress on user offers", from);
                return false;
            }



            Runtime.Notify("Transfer() transferValue", transferValue);
            if (transferValue <= 0)
            {
                // don't accept negative values
                Runtime.Notify("Transfer() transferValue was <= 0", transferValue);
                return false;
            }
            if (!Runtime.CheckWitness(from))
            {
                // ensure transaction is signed properly
                Runtime.Notify("Transfer() CheckWitness failed", from);
                return false;
            }
            if (from == to)
            {
                // don't waste resources when from==to
                Runtime.Notify("Transfer() from == to failed", to);
                return true;
            }
            BigInteger fromBalance = BalanceOf(from);                   // retrieve balance of originating account
            if (fromBalance < transferValue && !from.Equals(OWNER)) //To facilitate circular transfers, admin can transfer funds even if not available in circular processing stage. 
            {
                Runtime.Notify("Transfer() fromBalance < transferValue", fromBalance);
                // don't transfer if funds not available
                return false;
            }

            SetBalanceOf(from, fromBalance - transferValue);            // remove balance from originating account
            SetBalanceOf(to, BalanceOf(to) + transferValue);            // set new balance for destination account

            Transferred(from, to, transferValue);
            return true;
        }

        /**
         * set newBalance for address
         */
        private static void SetBalanceOf(byte[] address, BigInteger newBalance)
        {
            if (newBalance <= 0)
            {
                Runtime.Notify("SetBalanceOf() removing balance reference", newBalance);
                Delete(address);
            }
            else
            {
                Runtime.Notify("SetBalanceOf() setting balance", newBalance);
                Put(address, newBalance);
            }
        }

        /**
         * retrieve the number of tokens stored in address
         */
        public static BigInteger BalanceOf(byte[] address)
        {
            BigInteger currentBalance = Get(address).AsBigInteger();
            Runtime.Notify("BalanceOf() currentBalance", currentBalance);
            return currentBalance;
        }

        public static bool WithdrawCurrency(byte[] destinationAddress, BigInteger withdrawAmount)
        {

            if (IsProcessingInProgress() && isInvolvedInLockedTradeProcessing(destinationAddress))
            {

                // can not releaase funds as trades are bing processed on user accound
                Runtime.Notify("WithdrawCurrency() trades in progress on user offers", destinationAddress);
                return false;
            }



            if (!Runtime.CheckWitness(destinationAddress))
            {
                // ensure transaction is signed properly
                Runtime.Notify("WithdrawCurrency() CheckWitness failed", destinationAddress);
                return false;
            }

            Runtime.Notify("WithdrawCurrency() destinationAddress", destinationAddress);
            Runtime.Notify("WithdrawCurrency() withdrawAmount", withdrawAmount);

            BigInteger currentBalance = BalanceOf(destinationAddress);

            if (currentBalance <= 0 || currentBalance < withdrawAmount)
            {
                Runtime.Notify("WithdrawCurrency() insufficient funds", currentBalance);
                return false;
            }

            CurrencyWithdraw(destinationAddress, withdrawAmount);
            RefundAsset(destinationAddress, withdrawAmount);
            return true;
        }

        private static bool isInvolvedInLockedTradeProcessing(byte[] userId)
        {
            Chain chain = closingCircle.candidateChain;

            while (chain != null)
            {
                Offer offer = GetOfferFromStorage(chain.offerId); if (offer != null && offer.WalletAddress.Equals(userId))
                {
                    return true;

                }

                chain = chain.parentChain;
            }
            return false;
        }

        public static void CurrencyWithdraw(byte[] address, BigInteger takeFunds)
        {
            BigInteger currentBalance = BalanceOf(address);
            Runtime.Notify("CurrencyWithdraw() currentBalance", currentBalance);
            Runtime.Notify("CurrencyWithdraw() takeFunds", takeFunds);

            BigInteger updateBalance = currentBalance - takeFunds;
        }

        /**
         * determine whether or not the ico is still running and provide a bonus rate for the first 3 weeks
         */
        private static ulong IcoExchangeRate()
        {
            if (TotalSupply() >= MAX_SUPPLY)
            {
                return 0;
            }

            uint currentTimestamp = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp;
            int timeRunning = (int)currentTimestamp - ICO_START_TIME_STAMP;
            Runtime.Notify("ExchangeRate() timeRunning", timeRunning);

            if (currentTimestamp > ICO_END_TIMESTAMP || timeRunning < 0)
            {
                // ico period has not started or is ended
                return 0;
            }

            ulong bonusRate = 0;

            if (timeRunning < 1209600) //  first 2 weeks 
            {
                bonusRate = 25;
            }

            ulong exchangeRate = (ICO_BASE_EXCHANGE_RATE * (100 + bonusRate)) / 100;

            Runtime.Notify("ExchangeRate() bonusRate", bonusRate);
            Runtime.Notify("ExchangeRate() exchangeRate", exchangeRate);
            return exchangeRate;
        }
        //////////////////////////////////////////////////////////////////////////////////////////
        // END NEP5 implementation
        //////////////////////////////////////////////////////////////////////////////////////////
        #endregion  

        protected static void Put(byte[] key, byte[] value)
        {
            Storage.Put(Storage.CurrentContext, key, value);
        }



        protected static void Put(byte[] key, BigInteger value)
        {
            Storage.Put(Storage.CurrentContext, key, value);
        }

        protected static BigInteger GetAsBigInteger(byte[] key)
        {
            return Storage.Get(Storage.CurrentContext, key).AsBigInteger();
        }
        protected static byte[] Get(byte[] key)
        {
            return Storage.Get(Storage.CurrentContext, key);
        }
        private static void Delete(byte[] key)
        {
            Storage.Delete(Storage.CurrentContext, key);
        }
    }

    #region value objects to represent business objects
    public class Offer
    {


        public Offer(byte[] _Id, byte[] _WalletAddress, BigInteger _ValidTill, BigInteger _ItemsNetWorth, BigInteger _TotalAmount, byte[] _Currency, byte[][] _WantedIds, bool isBid = false)
        {
            this.Id = _Id;
            this.TotalAmount = _TotalAmount;
            this.ValidTill = 555;
            this.WantedIds = _WantedIds;
            this.WalletAddress = _WalletAddress;
            this.ItemsNetWorth = _ItemsNetWorth;
            this.Currency = _Currency; //nbx or usd
            this.IsBid = isBid;
        }

        public Offer() { }

        public byte[] WalletAddress;
        public byte[][] WantedIds;
        public byte[] Id;
        public BigInteger TotalAmount;
        public BigInteger ValidTill;
        public BigInteger ItemsNetWorth;
        public byte[] Currency; //nbx or usd
        public byte[] NextBid;
        public bool IsBid;
        public byte[] NextNbxOffer;
    }

    class Auction
    {

        public Auction(byte[] _Id, byte[] _WalletAddress, BigInteger _MinAmount, byte[] _Currency, BigInteger _ClosingAt)
        {
            this.Id = _Id;
            this.WalletAddress = _WalletAddress;
            this.MinAmount = _MinAmount;
            this.Currency = _Currency;
            this.ClosingAt = _ClosingAt;

        }

        public Auction()
        {
        }

        public byte[] Id;
        public byte[] WalletAddress;
        public BigInteger MinAmount;
        public byte[] Currency;
        public BigInteger ClosingAt;
        public byte[] Next;
        public byte[] Winner;
    }
    class Sale
    {


        public Sale(byte[] _Id, byte[] _WalletAddress, BigInteger _Amount, byte[] _currency, BigInteger _validTill)
        {
            this.Id = _Id;
            this.WalletAddress = _WalletAddress;
            this.Amount = _Amount;
            this.Currency = _currency;
            this.ValidTill = _validTill;

        }

        public Sale()
        {
        }

        public byte[] Id;
        public byte[] WalletAddress;
        public BigInteger Amount;
        public byte[] Currency;
        public BigInteger ValidTill;

        public byte[] Buyer;

    }

    #endregion


    public class Chain
    {
        public byte[] offerId;
        public Chain parentChain;
        public byte[] Catagory;
    }

}
