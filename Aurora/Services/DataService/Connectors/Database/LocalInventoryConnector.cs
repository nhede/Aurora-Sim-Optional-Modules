using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Aurora.Framework;
using Aurora.DataManager;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using Nini.Config;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using RegionFlags = Aurora.Framework.RegionFlags;

namespace Aurora.Services.DataService
{
    public class LocalInventoryConnector : IInventoryData
    {
        private IGenericData GD = null;
        private string m_foldersrealm = "inventoryfolders";
        private string m_itemsrealm = "inventoryitems";

        public void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore simBase, string defaultConnectionString)
        {
            if (source.Configs["AuroraConnectors"].GetString("AuthConnector", "LocalConnector") == "LocalConnector")
            {
                GD = GenericData;

                string connectionString = defaultConnectionString;
                if (source.Configs[Name] != null)
                    connectionString = source.Configs[Name].GetString("ConnectionString", defaultConnectionString);

                GD.ConnectToDatabase(connectionString, "Inventory", source.Configs["AuroraConnectors"].GetBoolean("ValidateTables", true));

                DataManager.DataManager.RegisterPlugin(Name, this);
            }
        }

        public string Name
        {
            get { return "IInventoryData"; }
        }

        public void Dispose()
        {
        }

        #region IInventoryData Members

        public List<InventoryFolderBase> GetFolders(string[] fields, string[] vals)
        {
            Dictionary<string, List<string>> retVal = GD.QueryNames (fields, vals, m_foldersrealm, "*");
            return ParseInventoryFolders (ref retVal);
        }

        public List<InventoryItemBase> GetItems(string[] fields, string[] vals)
        {
            string query = "";
            for (int i = 0; i < fields.Length; i++)
            {
                query += String.Format("{0} = '{1}' and ", fields[i], vals[i]);
                i++;
            }
            query = query.Remove(query.Length - 5);
            using (IDataReader reader = GD.QueryData(query, m_itemsrealm, "*"))
            {
                return ParseInventoryItems(reader);
            }
        }

        public OSDArray GetLLSDItems(string[] fields, string[] vals)
        {
            string query = "";
            for (int i = 0; i < fields.Length; i++)
            {
                query += String.Format("{0} = '{1}' and ", fields[i], vals[i]);
                i++;
            }
            query = query.Remove(query.Length - 5);
            using (IDataReader reader = GD.QueryData(query, m_itemsrealm, "*"))
            {
                return ParseLLSDInventoryItems(reader);
            }
        }

        private OSDArray ParseLLSDInventoryItems(IDataReader retVal)
        {
            OSDArray array = new OSDArray();

            while (retVal.Read())
            {
                for (int i = 0; i < retVal.FieldCount; i++)
                {
                    OSDMap item = new OSDMap();
                    OSDMap permissions = new OSDMap();
                    item["asset_id"] = UUID.Parse(retVal["assetID"].ToString());
                    item["name"] = retVal["inventoryName"].ToString();
                    item["desc"] = retVal["inventoryDescription"].ToString();
                    permissions["next_owner_mask"] = uint.Parse(retVal["inventoryNextPermissions"].ToString());
                    permissions["owner_mask"] = uint.Parse(retVal["inventoryCurrentPermissions"].ToString());
                    UUID creator;
                    if(UUID.TryParse(retVal["creatorID"].ToString(), out creator))
                        permissions["creator_id"] = creator;
                    else
                        permissions["creator_id"] = UUID.Zero;
                    permissions["base_mask"] = uint.Parse(retVal["inventoryBasePermissions"].ToString());
                    permissions["everyone_mask"] = uint.Parse(retVal["inventoryEveryOnePermissions"].ToString());
                    OSDMap sale_info = new OSDMap();
                    sale_info["sale_price"] = int.Parse(retVal["salePrice"].ToString());
                    switch (byte.Parse(retVal["saleType"].ToString()))
                    {
                        default:
                            sale_info["sale_type"] = "not";
                            break;
                        case 1:
                            sale_info["sale_type"] = "original";
                            break;
                        case 2:
                            sale_info["sale_type"] = "copy";
                            break;
                        case 3:
                            sale_info["sale_type"] = "contents";
                            break;
                    }
                    item["sale_info"] = sale_info;
                    item["created_at"] = int.Parse(retVal["creationDate"].ToString());
                    permissions["group_id"] = UUID.Parse(retVal["groupID"].ToString());
                    permissions["is_owner_group"] = int.Parse(retVal["groupOwned"].ToString()) == 1;
                    item["flags"] = uint.Parse(retVal["flags"].ToString());
                    item["item_id"] = UUID.Parse(retVal["inventoryID"].ToString());
                    item["parent_id"] = UUID.Parse(retVal["parentFolderID"].ToString());
                    permissions["group_mask"] = uint.Parse(retVal["inventoryGroupPermissions"].ToString());
                    item["agent_id"] = UUID.Parse(retVal["avatarID"].ToString());
                    permissions["owner_id"] = item["agent_id"];
                    permissions["last_owner_id"] = item["agent_id"];

                    item["type"] = Utils.AssetTypeToString((AssetType)int.Parse(retVal["assetType"].ToString()));
                    item["inv_type"] = Utils.InventoryTypeToString((InventoryType)int.Parse(retVal["invType"].ToString()));

                    item["permissions"] = permissions;

                    array.Add(item);
                }
            }
            retVal.Close();

            return array;
        }

        public byte[] FetchInventoryReply(OSDArray fetchRequest, UUID AgentID)
        {
            LLSDSerializationDictionary contents = new LLSDSerializationDictionary();
            contents.WriteStartMap("llsd"); //Start llsd

            contents.WriteKey("folders"); //Start array items
            contents.WriteStartArray("folders"); //Start array folders

            foreach (OSD m in fetchRequest)
            {
                contents.WriteStartMap("internalContents"); //Start internalContents kvp
                OSDMap invFetch = (OSDMap)m;

                //UUID agent_id = invFetch["agent_id"].AsUUID();
                UUID owner_id = invFetch["owner_id"].AsUUID();
                UUID folder_id = invFetch["folder_id"].AsUUID();
                bool fetch_folders = invFetch["fetch_folders"].AsBoolean();
                bool fetch_items = invFetch["fetch_items"].AsBoolean();
                int sort_order = invFetch["sort_order"].AsInteger();

                //Set the normal stuff
                contents["agent_id"] = AgentID;
                contents["owner_id"] = owner_id;
                contents["folder_id"] = folder_id;

                contents.WriteKey("items"); //Start array items
                contents.WriteStartArray("items"); 
                int count = 0;
                string query = String.Format("{0} = '{1}'", "parentFolderID", folder_id);
                using (IDataReader retVal = GD.QueryData(query, m_itemsrealm, "*"))
                {
                    while (retVal.Read())
                    {
                        for (int i = 0; i < retVal.FieldCount; i++)
                        {
                            contents.WriteStartMap("item"); //Start item kvp
                            contents["asset_id"] = UUID.Parse(retVal["assetID"].ToString());
                            contents["name"] = retVal["inventoryName"].ToString();
                            contents["desc"] = retVal["inventoryDescription"].ToString();


                            contents.WriteKey("permissions"); //Start permissions kvp
                            contents.WriteStartMap("permissions");
                            contents["group_id"] = UUID.Parse(retVal["groupID"].ToString());
                            contents["is_owner_group"] = int.Parse(retVal["groupOwned"].ToString()) == 1;
                            contents["group_mask"] = uint.Parse(retVal["inventoryGroupPermissions"].ToString());
                            contents["owner_id"] = UUID.Parse(retVal["avatarID"].ToString());
                            contents["last_owner_id"] = UUID.Parse(retVal["avatarID"].ToString());
                            contents["next_owner_mask"] = uint.Parse(retVal["inventoryNextPermissions"].ToString());
                            contents["owner_mask"] = uint.Parse(retVal["inventoryCurrentPermissions"].ToString());
                            UUID creator;
                            if (UUID.TryParse(retVal["creatorID"].ToString(), out creator))
                                contents["creator_id"] = creator;
                            else
                                contents["creator_id"] = UUID.Zero;
                            contents["base_mask"] = uint.Parse(retVal["inventoryBasePermissions"].ToString());
                            contents["everyone_mask"] = uint.Parse(retVal["inventoryEveryOnePermissions"].ToString());
                            contents.WriteEndMap(/*Permissions*/);

                            contents.WriteKey("sale_info"); //Start permissions kvp
                            contents.WriteStartMap("sale_info"); //Start sale_info kvp
                            contents["sale_price"] = int.Parse(retVal["salePrice"].ToString());
                            switch (byte.Parse(retVal["saleType"].ToString()))
                            {
                                default:
                                    contents["sale_type"] = "not";
                                    break;
                                case 1:
                                    contents["sale_type"] = "original";
                                    break;
                                case 2:
                                    contents["sale_type"] = "copy";
                                    break;
                                case 3:
                                    contents["sale_type"] = "contents";
                                    break;
                            }
                            contents.WriteEndMap(/*sale_info*/);


                            contents["created_at"] = int.Parse(retVal["creationDate"].ToString());
                            contents["flags"] = uint.Parse(retVal["flags"].ToString());
                            contents["item_id"] = UUID.Parse(retVal["inventoryID"].ToString());
                            contents["parent_id"] = UUID.Parse(retVal["parentFolderID"].ToString());
                            contents["agent_id"] = UUID.Parse(retVal["avatarID"].ToString());

                            contents["type"] = Utils.AssetTypeToString((AssetType)int.Parse(retVal["assetType"].ToString()));
                            contents["inv_type"] = Utils.InventoryTypeToString((InventoryType)int.Parse(retVal["invType"].ToString()));

                            count++;
                            contents.WriteEndMap(/*"item"*/); //end array items
                        }
                    }
                    retVal.Close();
                }
                contents.WriteEndArray(/*"items"*/); //end array items

                //TODO!
                int version = 0;

                contents.WriteStartArray("categories"); //We don't send any folders
                contents.WriteEndArray(/*"categories"*/);
                contents["descendents"] = count;
                contents["version"] = version;

                //Now add it to the folder array
                contents.WriteEndMap(); //end array internalContents
            }

            contents.WriteEndArray(); //end array folders
            contents.WriteEndMap(/*"llsd"*/); //end llsd

            return contents.GetSerializer();
        }

        public class LLSDSerializationDictionary
        {
            private MemoryStream sw = new MemoryStream();
            private XmlTextWriter writer;

            public LLSDSerializationDictionary()
            {
                writer = new XmlTextWriter(sw, Encoding.UTF8);
                writer.Formatting = Formatting.None;
                writer.WriteStartElement(String.Empty, "llsd", String.Empty);
            }

            public void WriteStartMap(string name)
            {
                writer.WriteStartElement(String.Empty, "map", String.Empty);
            }

            public void WriteEndMap()
            {
                writer.WriteEndElement();
            }

            public void WriteStartArray(string name)
            {
                writer.WriteStartElement(String.Empty, "array", String.Empty);
            }

            public void WriteEndArray()
            {
                writer.WriteEndElement();
            }

            public void WriteKey(string key)
            {
                writer.WriteStartElement(String.Empty, "key", String.Empty);
                writer.WriteString(key);
                writer.WriteEndElement();
            }

            public void WriteElement(object value)
            {
                Type t = value.GetType();
                if (t == typeof(bool))
                {
                    writer.WriteStartElement(String.Empty, "boolean", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(int))
                {
                    writer.WriteStartElement(String.Empty, "integer", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(int))
                {
                    writer.WriteStartElement(String.Empty, "integer", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(uint))
                {
                    writer.WriteStartElement(String.Empty, "integer", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(float))
                {
                    writer.WriteStartElement(String.Empty, "real", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(double))
                {
                    writer.WriteStartElement(String.Empty, "real", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(string))
                {
                    writer.WriteStartElement(String.Empty, "string", String.Empty);
                    writer.WriteValue(value);
                    writer.WriteEndElement();
                }
                else if (t == typeof(UUID))
                {
                    writer.WriteStartElement(String.Empty, "uuid", String.Empty);
                    writer.WriteValue(value.ToString()); //UUID has to be string!
                    writer.WriteEndElement();
                }
                else if (t == typeof(DateTime))
                {
                    writer.WriteStartElement(String.Empty, "date", String.Empty);
                    writer.WriteValue(AsString((DateTime)value));
                    writer.WriteEndElement();
                }
                else if (t == typeof(Uri))
                {
                    writer.WriteStartElement(String.Empty, "uri", String.Empty);
                    writer.WriteValue(((Uri)value).ToString());//URI has to be string
                    writer.WriteEndElement();
                }
                else if (t == typeof(byte[]))
                {
                    writer.WriteStartElement(String.Empty, "binary", String.Empty);
                    writer.WriteStartAttribute(String.Empty, "encoding", String.Empty);
                    writer.WriteString("base64");
                    writer.WriteEndAttribute();
                    writer.WriteValue(Convert.ToBase64String((byte[])value)); //Has to be base64
                    writer.WriteEndElement();
                }
            }

            public object this[string name]
            {
                set
                {
                    writer.WriteStartElement(String.Empty, "key", String.Empty);
                    writer.WriteString(name);
                    writer.WriteEndElement();
                    Type t = value.GetType();
                    if (t == typeof(bool))
                    {
                        writer.WriteStartElement(String.Empty, "boolean", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(int))
                    {
                        writer.WriteStartElement(String.Empty, "integer", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(int))
                    {
                        writer.WriteStartElement(String.Empty, "integer", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(uint))
                    {
                        writer.WriteStartElement(String.Empty, "integer", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(float))
                    {
                        writer.WriteStartElement(String.Empty, "real", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(double))
                    {
                        writer.WriteStartElement(String.Empty, "real", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(string))
                    {
                        writer.WriteStartElement(String.Empty, "string", String.Empty);
                        writer.WriteValue(value);
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(UUID))
                    {
                        writer.WriteStartElement(String.Empty, "uuid", String.Empty);
                        writer.WriteValue(value.ToString()); //UUID has to be string!
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(DateTime))
                    {
                        writer.WriteStartElement(String.Empty, "date", String.Empty);
                        writer.WriteValue(AsString((DateTime)value));
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(Uri))
                    {
                        writer.WriteStartElement(String.Empty, "uri", String.Empty);
                        writer.WriteValue(((Uri)value).ToString());//URI has to be string
                        writer.WriteEndElement();
                    }
                    else if (t == typeof(byte[]))
                    {
                        writer.WriteStartElement(String.Empty, "binary", String.Empty);
                        writer.WriteStartAttribute(String.Empty, "encoding", String.Empty);
                        writer.WriteString("base64");
                        writer.WriteEndAttribute();
                        writer.WriteValue(Convert.ToBase64String((byte[])value)); //Has to be base64
                        writer.WriteEndElement();
                    }
                }
            }

            public byte[] GetSerializer()
            {
                writer.WriteEndElement();
                writer.Close();

                return sw.ToArray();
            }

            private string AsString(DateTime value)
            {
                string format;
                if (value.Millisecond > 0)
                    format = "yyyy-MM-ddTHH:mm:ss.ffZ";
                else
                    format = "yyyy-MM-ddTHH:mm:ssZ";
                return value.ToUniversalTime().ToString(format);
            }
        }

        private List<InventoryFolderBase> ParseInventoryFolders(ref Dictionary<string, List<string>> retVal)
        {
            List<InventoryFolderBase> folders = new List<InventoryFolderBase> ();
            if (retVal.Count == 0)
                return folders;
            for (int i = 0; i < retVal.ElementAt(0).Value.Count; i++)
            {
                InventoryFolderBase folder = new InventoryFolderBase ();
                folder.Name = retVal["folderName"][i];
                folder.Type = short.Parse (retVal["type"][i]);
                folder.Version = (ushort)int.Parse (retVal["version"][i]);
                folder.ID = UUID.Parse (retVal["folderID"][i]);
                folder.Owner = UUID.Parse (retVal["agentID"][i]);
                folder.ParentID = UUID.Parse (retVal["parentFolderID"][i]);
                folders.Add (folder);
            }
            retVal.Clear();
            return folders;
        }

        private List<InventoryItemBase> ParseInventoryItems(IDataReader retVal)
        {
            List<InventoryItemBase> items = new List<InventoryItemBase> ();
            while (retVal.Read())
            {
                for (int i = 0; i < retVal.FieldCount; i++)
                {
                    InventoryItemBase item = new InventoryItemBase();
                    item.AssetID = UUID.Parse(retVal["assetID"].ToString());
                    item.AssetType = int.Parse(retVal["assetType"].ToString());
                    item.Name = retVal["inventoryName"].ToString();
                    item.Description = retVal["inventoryDescription"].ToString();
                    item.NextPermissions = uint.Parse(retVal["inventoryNextPermissions"].ToString());
                    item.CurrentPermissions = uint.Parse(retVal["inventoryCurrentPermissions"].ToString());
                    item.InvType = int.Parse(retVal["invType"].ToString());
                    item.CreatorId = retVal["creatorID"].ToString();
                    item.BasePermissions = uint.Parse(retVal["inventoryBasePermissions"].ToString());
                    item.EveryOnePermissions = uint.Parse(retVal["inventoryEveryOnePermissions"].ToString());
                    item.SalePrice = int.Parse(retVal["salePrice"].ToString());
                    item.SaleType = byte.Parse(retVal["saleType"].ToString());
                    item.CreationDate = int.Parse(retVal["creationDate"].ToString());
                    item.GroupID = UUID.Parse(retVal["groupID"].ToString());
                    item.GroupOwned = int.Parse(retVal["groupOwned"].ToString()) == 1;
                    item.Flags = uint.Parse(retVal["flags"].ToString());
                    item.ID = UUID.Parse(retVal["inventoryID"].ToString());
                    item.Owner = UUID.Parse(retVal["avatarID"].ToString());
                    item.Folder = UUID.Parse(retVal["parentFolderID"].ToString());
                    item.GroupPermissions = uint.Parse(retVal["inventoryGroupPermissions"].ToString());
                    items.Add(item);
                }
            }
            retVal.Close();
            return items;
        }

        public bool StoreFolder (InventoryFolderBase folder)
        {
            GD.Delete(m_foldersrealm, new string[1] { "folderID" }, new object[1] { folder.ID });
            return GD.Insert(m_foldersrealm, new string[6]{"folderName","type","version","folderID","agentID","parentFolderID"},
                new object[6]{folder.Name, folder.Type, folder.Version, folder.ID, folder.Owner, folder.ParentID});
        }

        public bool StoreItem (InventoryItemBase item)
        {
            GD.Delete(m_itemsrealm, new string[1] { "inventoryID" }, new object[1] { item.ID });
            return GD.Insert (m_itemsrealm, new string[20]{"assetID","assetType","inventoryName","inventoryDescription",
                "inventoryNextPermissions","inventoryCurrentPermissions","invType","creatorID","inventoryBasePermissions",
                "inventoryEveryOnePermissions","salePrice","saleType","creationDate","groupID","groupOwned",
                "flags","inventoryID","avatarID","parentFolderID","inventoryGroupPermissions"}, new object[20]{
                    item.AssetID, item.AssetType, item.Name, item.Description, item.NextPermissions, item.CurrentPermissions,
                    item.InvType, item.CreatorId, item.BasePermissions, item.EveryOnePermissions, item.SalePrice, item.SaleType,
                    item.CreationDate, item.GroupID, item.GroupOwned ? "1" : "0", item.Flags, item.ID, item.Owner,
                    item.Folder, item.GroupPermissions});
        }

        public bool DeleteFolders (string field, string val)
        {
            return GD.Delete (m_foldersrealm, new string[1] { field }, new object[1] { val });
        }

        public bool DeleteItems (string field, string val)
        {
            return GD.Delete (m_itemsrealm, new string[1] { field }, new object[1] { val });
        }

        public bool MoveItem (string id, string newParent)
        {
            return GD.Update (m_itemsrealm, new object[1] { newParent }, new string[1] { "parentFolderID" },
                new string[1] { "inventoryID" }, new object[1] { id });
        }

        public InventoryItemBase[] GetActiveGestures (UUID principalID)
        {
            string query = String.Format("{0} = '{1}' and {2} = '{3}'", "avatarID", principalID, "assetType", (int)AssetType.Gesture);

            using (IDataReader reader = GD.QueryData(query, m_itemsrealm, "*"))
            {
                List<InventoryItemBase> items = ParseInventoryItems(reader);
                items.RemoveAll(delegate(InventoryItemBase item)
                {
                    return !((item.Flags & 1) == 1); //1 means that it is active, so remove all ones that do not have a 1
                });
                return items.ToArray();
            }
        }

        public int GetAssetPermissions (UUID principalID, UUID assetID)
        {
            List<string> query = GD.Query (new string[2] { "avatarID", "assetID" }, new object[2] { principalID, assetID },
                m_itemsrealm, "inventoryCurrentPermissions");
            if (query.Count > 0)
                return int.Parse (query[0]);
            else
                return 0;
        }

        #endregion
    }
}
