﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenMetaverse;
using OpenSim.Framework;

namespace Aurora.Framework
{
    public interface IParcelServiceConnector
    {
        void StoreLandObject(LandData args);
        LandData GetLandData(UUID ParcelID);
        List<LandData> LoadLandObjects(UUID regionUUID);
        void RemoveLandObject(UUID ParcelID);
    }

	public interface IDirectoryServiceConnector
	{
		void AddLandObject(LandData args, UUID regionID, bool forSale, uint EstateID, bool showInSearch, UUID InfoUUID);
        AuroraLandData GetParcelInfo(UUID ParcelID);
        AuroraLandData[] GetParcelByOwner(UUID OwnerID);
        DirPlacesReplyData[] FindLand(string queryText, string category, int StartQuery);
		DirLandReplyData[] FindLandForSale(string searchType, string price, string area, int StartQuery);
		DirEventsReplyData[] FindEvents(string queryText, string flags, int StartQuery);
		DirEventsReplyData[] FindAllEventsInRegion(string regionName);
		DirClassifiedReplyData[] FindClassifieds(string queryText, string category, string queryFlags, int StartQuery);
		EventData GetEventInfo(string EventID);
		Classified[] GetClassifiedsInRegion(string regionName);
	}
}
