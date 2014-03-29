﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using DocumentServices.Modules.Readers.MsgReader.Header;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace DocumentServices.Modules.Readers.MsgReader.Outlook
{
    public class Storage : IDisposable
    {
        #region Public enum RecipientType
        /// <summary>
        /// Recipient types
        /// </summary>
        public enum RecipientType
        {
            /// <summary>
            /// Recipient is a To
            /// </summary>
            To,

            /// <summary>
            /// Recipient is a CC
            /// </summary>
            Cc,

            /// <summary>
            /// Recipient is a BCC
            /// </summary>
            Bcc,

            /// <summary>
            /// Recipient is unknown
            /// </summary>
            Unknown
        }
        #endregion

        #region Protected class NativeMethods
        /// <summary>
        /// Contains all the used native methods
        /// </summary>
        protected static class NativeMethods 
        {
            #region Stgm enum
            [Flags]
            public enum Stgm
            {
                Direct = 0x00000000,
                Transacted = 0x00010000,
                Simple = 0x08000000,
                Read = 0x00000000,
                Write = 0x00000001,
                Readwrite = 0x00000002,
                ShareDenyNone = 0x00000040,
                ShareDenyRead = 0x00000030,
                ShareDenyWrite = 0x00000020,
                ShareExclusive = 0x00000010,
                Priority = 0x00040000,
                Deleteonrelease = 0x04000000,
                Noscratch = 0x00100000,
                Create = 0x00001000,
                Convert = 0x00020000,
                Failifthere = 0x00000000,
                Nosnapshot = 0x00200000,
                DirectSwmr = 0x00400000
            }
            #endregion

            #region DllImports
            [DllImport("ole32.DLL")]
            internal static extern int CreateILockBytesOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease,
                out ILockBytes ppLkbyt);

            [DllImport("ole32.DLL")]
            internal static extern int StgIsStorageILockBytes(ILockBytes plkbyt);

            [DllImport("ole32.DLL")]
            internal static extern int StgCreateDocfileOnILockBytes(ILockBytes plkbyt, Stgm grfMode, uint reserved,
                out IStorage ppstgOpen);

            [DllImport("ole32.DLL")]
            internal static extern void StgOpenStorageOnILockBytes(ILockBytes plkbyt, IStorage pstgPriority, Stgm grfMode,
                IntPtr snbExclude, uint reserved,
                out IStorage ppstgOpen);

            [DllImport("ole32.DLL")]
            internal static extern int StgIsStorageFile([MarshalAs(UnmanagedType.LPWStr)] string wcsName);

            [DllImport("ole32.DLL")]
            internal static extern int StgOpenStorage([MarshalAs(UnmanagedType.LPWStr)] string wcsName,
                IStorage pstgPriority, Stgm grfMode, IntPtr snbExclude, int reserved,
                out IStorage ppstgOpen);
            #endregion

            #region CloneStorage
            internal static IStorage CloneStorage(IStorage source, bool closeSource)
            {
                IStorage memoryStorage = null;
                ILockBytes memoryStorageBytes = null;
                try
                {
                    //create a ILockBytes (unmanaged byte array) and then create a IStorage using the byte array as a backing store
                    CreateILockBytesOnHGlobal(IntPtr.Zero, true, out memoryStorageBytes);
                    StgCreateDocfileOnILockBytes(memoryStorageBytes, Stgm.Create | Stgm.Readwrite | Stgm.ShareExclusive,
                        0, out memoryStorage);

                    //copy the source storage into the new storage
                    source.CopyTo(0, null, IntPtr.Zero, memoryStorage);
                    memoryStorageBytes.Flush();
                    memoryStorage.Commit(0);

                    // Ensure memory is released
                    ReferenceManager.AddItem(memoryStorage);
                }
                catch
                {
                    if (memoryStorage != null)
                        Marshal.ReleaseComObject(memoryStorage);
                }
                finally
                {
                    if (memoryStorageBytes != null)
                        Marshal.ReleaseComObject(memoryStorageBytes);

                    if (closeSource)
                        Marshal.ReleaseComObject(source);
                }

                return memoryStorage;
            }
            #endregion

            #region Nested type: IEnumSTATSTG
            [ComImport, Guid("0000000D-0000-0000-C000-000000000046"),
             InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IEnumSTATSTG
            {
                void Next(uint celt, [MarshalAs(UnmanagedType.LPArray), Out] STATSTG[] rgelt, out uint pceltFetched);
                void Skip(uint celt);
                void Reset();

                [return: MarshalAs(UnmanagedType.Interface)]
                IEnumSTATSTG Clone();
            }
            #endregion

            #region Nested type: ILockBytes
            [ComImport, Guid("0000000A-0000-0000-C000-000000000046"),
             InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            internal interface ILockBytes
            {
                void ReadAt([In, MarshalAs(UnmanagedType.U8)] long ulOffset,
                    [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] pv,
                    [In, MarshalAs(UnmanagedType.U4)] int cb,
                    [Out, MarshalAs(UnmanagedType.LPArray)] int[] pcbRead);

                void WriteAt([In, MarshalAs(UnmanagedType.U8)] long ulOffset,
                    [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] pv,
                    [In, MarshalAs(UnmanagedType.U4)] int cb,
                    [Out, MarshalAs(UnmanagedType.LPArray)] int[] pcbWritten);

                void Flush();
                void SetSize([In, MarshalAs(UnmanagedType.U8)] long cb);

                void LockRegion([In, MarshalAs(UnmanagedType.U8)] long libOffset,
                    [In, MarshalAs(UnmanagedType.U8)] long cb,
                    [In, MarshalAs(UnmanagedType.U4)] int dwLockType);

                void UnlockRegion([In, MarshalAs(UnmanagedType.U8)] long libOffset,
                    [In, MarshalAs(UnmanagedType.U8)] long cb,
                    [In, MarshalAs(UnmanagedType.U4)] int dwLockType);

                void Stat([Out] out STATSTG pstatstg, [In, MarshalAs(UnmanagedType.U4)] int grfStatFlag);
            }
            #endregion

            #region Nested type: IStorage
            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
             Guid("0000000B-0000-0000-C000-000000000046")]
            public interface IStorage
            {
                [return: MarshalAs(UnmanagedType.Interface)]
                IStream CreateStream([In, MarshalAs(UnmanagedType.BStr)] string pwcsName,
                    [In, MarshalAs(UnmanagedType.U4)] Stgm grfMode,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved1,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved2);

                [return: MarshalAs(UnmanagedType.Interface)]
                IStream OpenStream([In, MarshalAs(UnmanagedType.BStr)] string pwcsName, IntPtr reserved1,
                    [In, MarshalAs(UnmanagedType.U4)] Stgm grfMode,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved2);

                [return: MarshalAs(UnmanagedType.Interface)]
                IStorage CreateStorage([In, MarshalAs(UnmanagedType.BStr)] string pwcsName,
                    [In, MarshalAs(UnmanagedType.U4)] Stgm grfMode,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved1,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved2);

                [return: MarshalAs(UnmanagedType.Interface)]
                IStorage OpenStorage([In, MarshalAs(UnmanagedType.BStr)] string pwcsName, IntPtr pstgPriority,
                    [In, MarshalAs(UnmanagedType.U4)] Stgm grfMode, IntPtr snbExclude,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved);

                void CopyTo(int ciidExclude, [In, MarshalAs(UnmanagedType.LPArray)] Guid[] pIidExclude,
                    IntPtr snbExclude, [In, MarshalAs(UnmanagedType.Interface)] IStorage stgDest);

                void MoveElementTo([In, MarshalAs(UnmanagedType.BStr)] string pwcsName,
                    [In, MarshalAs(UnmanagedType.Interface)] IStorage stgDest,
                    [In, MarshalAs(UnmanagedType.BStr)] string pwcsNewName,
                    [In, MarshalAs(UnmanagedType.U4)] int grfFlags);

                void Commit(int grfCommitFlags);
                void Revert();

                void EnumElements([In, MarshalAs(UnmanagedType.U4)] int reserved1, IntPtr reserved2,
                    [In, MarshalAs(UnmanagedType.U4)] int reserved3,
                    [MarshalAs(UnmanagedType.Interface)] out IEnumSTATSTG ppVal);

                void DestroyElement([In, MarshalAs(UnmanagedType.BStr)] string pwcsName);

                void RenameElement([In, MarshalAs(UnmanagedType.BStr)] string pwcsOldName,
                    [In, MarshalAs(UnmanagedType.BStr)] string pwcsNewName);

                void SetElementTimes([In, MarshalAs(UnmanagedType.BStr)] string pwcsName, [In] FILETIME pctime,
                    [In] FILETIME patime, [In] FILETIME pmtime);

                void SetClass([In] ref Guid clsid);
                void SetStateBits(int grfStateBits, int grfMask);
                void Stat([Out] out STATSTG pStatStg, int grfStatFlag);
            }
            #endregion
        }
        #endregion

        #region Private nested class ReferenceManager
        /// <summary>
        /// Class used to track all the COM objects
        /// </summary>
        private class ReferenceManager
        {
            /// <summary>
            /// Static instance to the <see cref="ReferenceManager"/>
            /// </summary>
            private static readonly ReferenceManager Instance = new ReferenceManager();

            /// <summary>
            /// Used to keep track of all the objects
            /// </summary>
            private readonly List<object> _trackingObjects = new List<object>();

            /// <summary>
            /// Add an object to track
            /// </summary>
            /// <param name="track"></param>
            public static void AddItem(object track)
            {
                lock (Instance)
                {
                    if (!Instance._trackingObjects.Contains(track))
                        Instance._trackingObjects.Add(track);
                }
            }

            /// <summary>
            /// Remove a tracked object
            /// </summary>
            /// <param name="track"></param>
            public static void RemoveItem(object track)
            {
                lock (Instance)
                {
                    if (Instance._trackingObjects.Contains(track))
                        Instance._trackingObjects.Remove(track);
                }
            }

            /// <summary>
            /// Dispose all tracked objects
            /// </summary>
            ~ReferenceManager()
            {
                foreach (var trackingObject in _trackingObjects)
                    Marshal.ReleaseComObject(trackingObject);
            }
        }
        #endregion

        #region Public nested class Attachment
        /// <summary>
        /// Class represents a MSG attachment
        /// </summary>
        public class Attachment : Storage
        {
            #region Properties
            /// <summary>
            /// Gets the filename.
            /// </summary>
            /// <value> The filename. </value>
            public string Filename
            {
                get
                {
                    var filename = GetMapiPropertyString(Consts.PrAttachLongFilename);
                    
                    if (string.IsNullOrEmpty(filename))
                        filename = GetMapiPropertyString(Consts.PrAttachFilename);
                    
                    if (string.IsNullOrEmpty(filename))
                        filename = GetMapiPropertyString(Consts.PrDisplayName);
                    
                    return filename;
                }
            }

            /// <summary>
            /// Gets the data.
            /// </summary>
            /// <value> The data. </value>
            public byte[] Data
            {
                get { return GetMapiPropertyBytes(Consts.PrAttachData); }
            }

            /// <summary>
            /// Gets the content id.
            /// </summary>
            /// <value> The content id. </value>
            public string ContentId
            {
                get { return GetMapiPropertyString(Consts.PrAttachContentId); }
            }

            /// <summary>
            /// Gets the rendering posisiton.
            /// </summary>
            /// <value> The rendering posisiton. </value>
            public int RenderingPosisiton
            {
                get { return GetMapiPropertyInt32(Consts.PrRenderingPosition); }
            }
            #endregion

            #region Constructor
            /// <summary>
            /// Initializes a new instance of the <see cref="Storage.Attachment" /> class.
            /// </summary>
            /// <param name="message"> The message. </param>
            public Attachment(Storage message) : base(message._storage)
            {
                GC.SuppressFinalize(message);
                _propHeaderSize = Consts.PropertiesStreamHeaderAttachOrRecip;
            }
            #endregion
        }
        #endregion

        #region Public nested class Message
        /// <summary>
        /// Class represent a MSG object
        /// </summary>
        public class Message : Storage
        {
            #region Fields
            /// <summary>
            /// Containts any attachments
            /// </summary>
            private readonly List<Object> _attachments = new List<Object>();

            /// <summary>
            /// Containts any MSG attachments
            /// </summary>
            //private readonly List<Message> _messages = new List<Message>();

            /// <summary>
            /// Containts all the recipients
            /// </summary>
            private readonly List<Recipient> _recipients = new List<Recipient>();

            /// <summary>
            /// Contains flag information
            /// </summary>
            public Flag _flag;

            /// <summary>
            /// Contains task information when a flag is set on a MSG object
            /// </summary>
            private Task _task;
            #endregion

            #region Properties
            /// <summary>
            /// Gives the Message class type that is used e.g. IPM.Note (E-mail) or IPM.Appointment (Agenda)
            /// </summary>
            public string Type
            {
                get { return GetMapiPropertyString(Consts.PrMessageClass); }
            }

            /// <summary>
            /// Gets the list of recipients in the outlook message.
            /// </summary>
            public List<Recipient> Recipients
            {
                get { return _recipients; }
            }

            /// <summary>
            /// Gets the list of attachments in the outlook message.
            /// </summary>
            public List<Object> Attachments
            {
                get { return _attachments; }
            }

            ///// <summary>
            ///// Gets the list of sub messages in the outlook message.
            ///// </summary>
            ///// <value>The list of sub messages in the outlook message</value>
            //public List<Message> Messages
            //{
            //    get { return _messages; }
            //}

            /// <summary>
            /// Gets the display value of the contact that sent the email.
            /// </summary>
            public Sender Sender { get; private set; }

            /// <summary>
            /// Gives the aviable E-mail headers. These are only filled when the message
            /// has been sent accross the internet. This will be null when there aren't
            /// any message headers
            /// </summary>
            public MessageHeader Headers { get; private set; }

            /// <summary>
            /// Gets the date/time in UTC format when the message is sent.
            /// Null when not available
            /// </summary>
            public DateTime? SentOn
            {
                get
                {
                    var sentOn = GetMapiPropertyDateTime(Consts.PrProviderSubmitTime) ??
                                 GetMapiPropertyDateTime(Consts.PrClientSubmitTime);

                    if (sentOn != null)
                        return sentOn;

                    if (Headers != null)
                        return Headers.DateSent.ToLocalTime();

                    return null;
                }
            }

            /// <summary>
            /// PR_MESSAGE_DELIVERY_TIME  is the time that the message was delivered to the store and 
            /// PR_CLIENT_SUBMIT_TIME  is the time when the message was sent by the client (Outlook) to the server.
            /// Now in this case when the Outlook is offline, it refers to the local store. Therefore when an email is sent, 
            /// it gets submitted to the local store and PR_MESSAGE_DELIVERY_TIME  gets set the that time. Once the Outlook is 
            /// online at that point the message gets submitted by the client to the server and the PR_CLIENT_SUBMIT_TIME  gets stamped. 
            /// </summary>
            public DateTime? ReceivedOn
            {
                get
                {
                    var receivedOn = GetMapiPropertyDateTime(Consts.PrMessageDeliveryTime);

                    if (receivedOn != null)
                        return receivedOn;

                    if (Headers != null && Headers.Received != null && Headers.Received.Count > 0)
                        return Headers.Received[0].Date.ToLocalTime();

                    return null;
                }
            }

            /// <summary>
            /// Gets the subject of the outlook message.
            /// </summary>
            public string Subject
            {
                get { return GetMapiPropertyString(Consts.PrSubject); }
            }

            public Flag Flag 
            {
                get
                {
                    if (_flag != null)
                        return _flag;

                    var flag = new Flag(this);
                    
                    if (flag.Request != null)
                        _flag = flag;
                    
                    return _flag;                        
                }
            }

            /// <summary>
            /// Get information about the task that is set on an E-mail msg file or when the MSG is a agenda item.
            /// This property is null when there is no <see cref="Flag"/> set on the E-mail msg object.
            /// </summary>
            public Task Task
            {
                get
                {
                    if (_task != null)
                        return _task;

                    if (_flag == null)
                        return null;

                    _task = new Task(this);
                    return _task;
                }
            }

            /// <summary>
            /// Gives the categories that are placed in the outlook message.
            /// Only supported for outlook messages from Outlook 2007 or higher
            /// </summary>
            public List<string> Categories
            {
                get { return GetMapiProperty(Consts.PidNameKeywords) as List<string>; }
            }

            /// <summary>
            /// Gets the body of the outlook message in plain text format.
            /// </summary>
            /// <value> The body of the outlook message in plain text format. </value>
            public string BodyText
            {
                get { return GetMapiPropertyString(Consts.PrBody); }
            }

            /// <summary>
            /// Gets the body of the outlook message in RTF format.
            /// </summary>
            /// <value> The body of the outlook message in RTF format. </value>
            public string BodyRtf
            {
                get
                {
                    //get value for the RTF compressed MAPI property
                    var rtfBytes = GetMapiPropertyBytes(Consts.PrRtfCompressed);

                    //return null if no property value exists
                    if (rtfBytes == null || rtfBytes.Length == 0)
                        return null;

                    //decompress the rtf value
                    rtfBytes = RtfDecompressor.DecompressRtf(rtfBytes);

                    //encode the rtf value as an ascii string and return
                    return Encoding.ASCII.GetString(rtfBytes);
                }
            }

            /// <summary>
            /// Gets the body of the outlook message in HTML format.
            /// </summary>
            /// <value> The body of the outlook message in HTML format. </value>
            public string BodyHtml
            {
                get
                {
                    //get value for the HTML MAPI property
                    var html = GetMapiPropertyString(Consts.PrBodyHtml);

                    // Als er geen HTML gedeelte is gevonden
                    if (html == null)
                    {
                        // Check if we have html embedded into rtf
                        var bodyRtf = BodyRtf;
                        if (bodyRtf != null)
                        {
                            var rtfDomDocument = new Rtf.DomDocument();
                            rtfDomDocument.LoadRtfText(bodyRtf);
                            if (!string.IsNullOrEmpty(rtfDomDocument.HtmlContent))
                                return rtfDomDocument.HtmlContent;
                        }

                        return null;
                    }

                    return html;
                }
            }
            #endregion

            #region Constructors
            /// <summary>
            ///   Initializes a new instance of the <see cref="Storage.Message" /> class from a msg file.
            /// </summary>
            /// <param name="msgfile">The msg file to load</param>
            public Message(string msgfile) : base(msgfile) {}

            /// <summary>
            /// Initializes a new instance of the <see cref="Storage.Message" /> class from a <see cref="Stream" /> containing an IStorage.
            /// </summary>
            /// <param name="storageStream"> The <see cref="Stream" /> containing an IStorage. </param>
            public Message(Stream storageStream) : base(storageStream) {}

            /// <summary>
            /// Initializes a new instance of the <see cref="Storage.Message" /> class on the specified <see> <cref>NativeMethods.IStorage</cref> </see>.
            /// </summary>
            /// <param name="storage"> The storage to create the <see cref="Storage.Message" /> on. </param>
            private Message(NativeMethods.IStorage storage) : base(storage)
            {
                _propHeaderSize = Consts.PropertiesStreamHeaderTop;
            }
            #endregion

            #region GetHeaders
            /// <summary>
            /// Try's to read the E-mail transport headers. They are only there when a msg file has been
            /// sent over the internet. When a message stays inside an Exchange server there are not any headers
            /// </summary>
            private void GetHeaders()
            {
                // According to Microsoft the headers should be in PrTransportMessageHeaders1
                // but in my case they are always in PrTransportMessageHeaders2 ... meaby that this
                // has something to do with that I use Outlook 2010??
                var headersString = GetMapiPropertyString(Consts.PrTransportMessageHeaders1);
                if (string.IsNullOrEmpty(headersString))
                    headersString = GetMapiPropertyString(Consts.PrTransportMessageHeaders2);

                if (!string.IsNullOrEmpty(headersString))
                    Headers = HeaderExtractor.GetHeaders(headersString);
            }
            #endregion

            #region LoadStorage
            /// <summary>
            /// Processes sub storages on the specified storage to capture attachment and recipient data.
            /// </summary>
            /// <param name="storage"> The storage to check for attachment and recipient data. </param>
            protected override void LoadStorage(NativeMethods.IStorage storage)
            {
                base.LoadStorage(storage);
                Sender = new Sender(new Storage(storage));
                GetHeaders();

                foreach (var storageStat in SubStorageStatistics.Values)
                {
                    //element is a storage. get it and add its statistics object to the sub storage dictionary
                    var subStorage = storage.OpenStorage(storageStat.pwcsName, IntPtr.Zero, NativeMethods.Stgm.Read | NativeMethods.Stgm.ShareExclusive,
                        IntPtr.Zero, 0);


                    //run specific load method depending on sub storage name prefix
                    if (storageStat.pwcsName.StartsWith(Consts.RecipStoragePrefix))
                    {
                        var recipient = new Recipient(new Storage(subStorage));
                        _recipients.Add(recipient);
                    }
                    else if (storageStat.pwcsName.StartsWith(Consts.AttachStoragePrefix))
                    {
                        LoadAttachmentStorage(subStorage);
                    }
                    else
                    {
                        //release sub storage
                        Marshal.ReleaseComObject(subStorage);
                    }
                }
            }

            /// <summary>
            /// Loads the attachment data out of the specified storage.
            /// </summary>
            /// <param name="storage"> The attachment storage. </param>
            private void LoadAttachmentStorage(NativeMethods.IStorage storage)
            {
                //create attachment from attachment storage
                var attachment = new Attachment(new Storage(storage));

                //if attachment is a embeded msg handle differently than an normal attachment
                var attachMethod = attachment.GetMapiPropertyInt32(Consts.PrAttachMethod);
                if (attachMethod == Consts.AttachEmbeddedMsg)
                {
                    //create new Message and set parent and header size
                    var subMsg = new Message(attachment.GetMapiProperty(Consts.PrAttachData) as NativeMethods.IStorage) { _parentMessage = this, _propHeaderSize = Consts.PropertiesStreamHeaderEmbeded };
                    _attachments.Add(subMsg);
                    //add to messages list
                    //_messages.Add(subMsg);
                }
                else
                {
                    //add attachment to attachment list
                    _attachments.Add(attachment);
                }
            }
            #endregion

            #region Save
            /// <summary>
            /// Saves this <see cref="Storage.Message" /> to the specified file name.
            /// </summary>
            /// <param name="fileName"> Name of the file. </param>
            public void Save(string fileName)
            {
                var saveFileStream = File.Open(fileName, FileMode.Create, FileAccess.ReadWrite);
                Save(saveFileStream);
                saveFileStream.Close();
            }

            /// <summary>
            /// Saves this <see cref="Storage.Message" /> to the specified stream.
            /// </summary>
            /// <param name="stream"> The stream to save to. </param>
            public void Save(Stream stream)
            {
                //get statistics for stream 
                Storage saveMsg = this;

                NativeMethods.IStorage memoryStorage = null;
                NativeMethods.IStorage nameIdSourceStorage = null;
                NativeMethods.ILockBytes memoryStorageBytes = null;
                try
                {
                    //create a ILockBytes (unmanaged byte array) and then create a IStorage using the byte array as a backing store
                    NativeMethods.CreateILockBytesOnHGlobal(IntPtr.Zero, true, out memoryStorageBytes);
                    NativeMethods.StgCreateDocfileOnILockBytes(memoryStorageBytes, NativeMethods.Stgm.Create | NativeMethods.Stgm.Readwrite | NativeMethods.Stgm.ShareExclusive, 0, out memoryStorage);

                    //copy the save storage into the new storage
                    saveMsg._storage.CopyTo(0, null, IntPtr.Zero, memoryStorage);
                    memoryStorageBytes.Flush();
                    memoryStorage.Commit(0);

                    //if not the top parent then the name id mapping needs to be copied from top parent to this message and the property stream header needs to be padded by 8 bytes
                    if (!IsTopParent)
                    {
                        //create a new name id storage and get the source name id storage to copy from
                        var nameIdStorage = memoryStorage.CreateStorage(Consts.NameidStorage, NativeMethods.Stgm.Create | NativeMethods.Stgm.Readwrite | NativeMethods.Stgm.ShareExclusive, 0, 0);
                        nameIdSourceStorage = TopParent._storage.OpenStorage(Consts.NameidStorage, IntPtr.Zero, NativeMethods.Stgm.Read | NativeMethods.Stgm.ShareExclusive,
                            IntPtr.Zero, 0);

                        //copy the name id storage from the parent to the new name id storage
                        nameIdSourceStorage.CopyTo(0, null, IntPtr.Zero, nameIdStorage);

                        //get the property bytes for the storage being copied
                        var props = saveMsg.GetStreamBytes(Consts.PropertiesStream);

                        //create new array to store a copy of the properties that is 8 bytes larger than the old so the header can be padded
                        var newProps = new byte[props.Length + 8];

                        //insert 8 null bytes from index 24 to 32. this is because a top level object property header requires a 32 byte header
                        Buffer.BlockCopy(props, 0, newProps, 0, 24);
                        Buffer.BlockCopy(props, 24, newProps, 32, props.Length - 24);

                        //remove the copied prop bytes so it can be replaced with the padded version
                        memoryStorage.DestroyElement(Consts.PropertiesStream);

                        //create the property stream again and write in the padded version
                        var propStream = memoryStorage.CreateStream(Consts.PropertiesStream, NativeMethods.Stgm.Readwrite | NativeMethods.Stgm.ShareExclusive, 0, 0);
                        propStream.Write(newProps, newProps.Length, IntPtr.Zero);
                    }

                    //commit changes to the storage
                    memoryStorage.Commit(0);
                    memoryStorageBytes.Flush();

                    //get the STATSTG of the ILockBytes to determine how many bytes were written to it
                    STATSTG memoryStorageBytesStat;
                    memoryStorageBytes.Stat(out memoryStorageBytesStat, 1);

                    //read the bytes into a managed byte array
                    var memoryStorageContent = new byte[memoryStorageBytesStat.cbSize];
                    memoryStorageBytes.ReadAt(0, memoryStorageContent, memoryStorageContent.Length, null);

                    //write storage bytes to stream
                    stream.Write(memoryStorageContent, 0, memoryStorageContent.Length);
                }
                finally
                {
                    if (nameIdSourceStorage != null)
                    {
                        Marshal.ReleaseComObject(nameIdSourceStorage);
                    }

                    if (memoryStorage != null)
                    {
                        Marshal.ReleaseComObject(memoryStorage);
                    }

                    if (memoryStorageBytes != null)
                    {
                        Marshal.ReleaseComObject(memoryStorageBytes);
                    }
                }
            }
            #endregion

            #region Disposing
            protected override void Disposing()
            {
                // Dispose sub storages
                foreach (var recipient in _recipients)
                    recipient.Dispose();

                // Dispose sub storages
                foreach (var attachment in _attachments)
                {
                    if (attachment.GetType() == typeof (Attachment))
                        ((Attachment) attachment).Dispose();
                    else if (attachment.GetType() == typeof(Message))
                        ((Message)attachment).Dispose();
                }
            }
            #endregion
        }
        #endregion

        #region Public nested class Sender
        /// <summary>
        /// Class used to contain the Sender
        /// </summary>
        public class Sender : Storage
        {
            #region Properties
            /// <summary>
            /// Gets the display value of the contact that sent the email.
            /// </summary>
            public string DisplayName
            {
                get { return GetMapiPropertyString(Consts.PrSenderName); }
            }

            /// <summary>
            /// Gets the sender email
            /// </summary>
            public string Email
            {
                get
                {
                    var eMail = GetMapiPropertyString(Consts.PrSenderEmailAddress);

                    if (string.IsNullOrEmpty(eMail) || eMail.IndexOf('@') < 0)
                        eMail = GetMapiPropertyString(Consts.PrSenderEmailAddress2);

                    if (string.IsNullOrEmpty(eMail) || eMail.IndexOf("@", StringComparison.Ordinal) < 0)
                    {
                        // Get address from email header
                        var header = GetStreamAsString(Consts.HeaderStreamName, Encoding.Unicode);
                        var m = Regex.Match(header, "From:.*<(?<email>.*?)>");
                        eMail = m.Groups["email"].ToString();
                    }

                    return eMail;
                }
            }
            #endregion

            #region Constructor
            /// <summary>
            /// Initializes a new instance of the <see cref="Storage.Sender" /> class.
            /// </summary>
            /// <param name="message"> The message. </param>
            public Sender(Storage message) : base(message._storage)
            {
                GC.SuppressFinalize(message);
                _propHeaderSize = Consts.PropertiesStreamHeaderAttachOrRecip;
            }
            #endregion
        }
        #endregion

        #region Public nested class Recipient
        /// <summary>
        /// Class used to contain To, CC and BCC recipients
        /// </summary>
        public class Recipient : Storage
        {
            #region Properties
            /// <summary>
            /// Gets the display name
            /// </summary>
            public string DisplayName
            {
                get { return GetMapiPropertyString(Consts.PrDisplayName); }
            }

            /// <summary>
            /// Gets the recipient email
            /// </summary>
            public string Email
            {
                get
                {
                    var email = GetMapiPropertyString(Consts.PrEmail);

                    if (string.IsNullOrEmpty(email))
                        email = GetMapiPropertyString(Consts.PrEmail2);

                    return email;
                }
            }

            /// <summary>
            /// Gets the recipient type
            /// </summary>
            public RecipientType Type
            {
                get
                {
                    var recipientType = GetMapiPropertyInt32(Consts.PrRecipientType);
                    switch (recipientType)
                    {
                        case Consts.MapiTo:
                            return RecipientType.To;

                        case Consts.MapiCc:
                            return RecipientType.Cc;

                        case Consts.MapiBcc:
                            return RecipientType.Bcc;

                        default:
                            return RecipientType.Unknown;
                    }
                }
            }
            #endregion

            #region Constructor
            /// <summary>
            ///   Initializes a new instance of the <see cref="Storage.Recipient" /> class.
            /// </summary>
            /// <param name="message"> The message. </param>
            public Recipient(Storage message) : base(message._storage)
            {
                GC.SuppressFinalize(message);
                _propHeaderSize = Consts.PropertiesStreamHeaderAttachOrRecip;
            }
            #endregion
        }
        #endregion

        #region Public nested class Flag
        /// <summary>
        /// Class used to contain all the flag (follow up) information.
        /// </summary>
        public class Flag : Storage
        {
            #region Public enum FlagStatus
            public enum FlagStatus
            {
                /// <summary>
                /// The msg object has been flagged as completed
                /// </summary>
                Complete = 1,

                /// <summary>
                /// The msg object has been flagged and marked as a task
                /// </summary>
                Marked = 2
            }
            #endregion

            #region Properties
            /// <summary>
            /// Returns the flag request text
            /// </summary>
            public string Request
            {
                get { return GetMapiPropertyString(Consts.FlagRequest); }
            }

            /// <summary>
            /// Returns the <see cref="FlagStatus">Status</see> of the flag
            /// </summary>
            public FlagStatus? Status
            {
                get { return (FlagStatus) GetMapiPropertyInt32(Consts.PrFlagStatus); }
            }
            #endregion

            #region Constructor
            /// <summary>
            ///   Initializes a new instance of the <see cref="Storage.Flag" /> class.
            /// </summary>
            /// <param name="message"> The message. </param>
            public Flag(Storage message) : base(message._storage)
            {
                GC.SuppressFinalize(message);
                _propHeaderSize = Consts.PropertiesStreamHeaderTop;
            }
            #endregion
        }
        #endregion

        #region Public nested class Task
        /// <summary>
        /// Class used to contain all the task information. A task can also be added to a E-mail (MSG) when
        /// the FollowUp flag is set.
        /// </summary>
        public class Task : Storage
        {
            #region Enum TaskStatus
            public enum TaskStatus
            {
                /// <summary>
                /// The task has not yet started
                /// </summary>
                NotStarted = 0,

                /// <summary>
                /// The task is in progress
                /// </summary>
                InProgess = 1,

                /// <summary>
                /// The task is complete
                /// </summary>
                Complete = 2,

                /// <summary>
                /// The task is waiting on someone else
                /// </summary>
                Waiting = 3
            }
            #endregion

            #region Properties
            /// <summary>
            /// Returns the start datetime of the task
            /// </summary>
            public DateTime? StartDate
            {
                get { return GetMapiPropertyDateTime(Consts.TaskStartDate); }
            }

            /// <summary>
            /// Returns the due datetime of the task
            /// </summary>
            public DateTime? DueDate
            {
                get { return GetMapiPropertyDateTime(Consts.TaskDueDate); }    
            }

            /// <summary>
            /// Returns the <see cref="TaskStatus">Status</see> of the task
            /// </summary>
            public TaskStatus? Status
            {
                get { return (TaskStatus) GetMapiPropertyInt32(Consts.TaskStatus); }
            }

            /// <summary>
            /// Returns true when the task has been completed
            /// </summary>
            public bool? Complete
            {
                get { return GetMapiPropertyBool(Consts.TaskComplete); }    
            }

            /// <summary>
            /// Returns the datetime when the task was completed, only set when <see cref="Complete"/> is true
            /// </summary>
            public DateTime? CompleteTime
            {
                get { return GetMapiPropertyDateTime(Consts.PrFlagCompleteTime); }
            }
            #endregion

            #region Constructor
            /// <summary>
            /// Initializes a new instance of the <see cref="Storage.Task" /> class.
            /// </summary>
            /// <param name="message"> The message. </param>
            public Task(Storage message) : base(message._storage)
            {
                GC.SuppressFinalize(message);
                _propHeaderSize = Consts.PropertiesStreamHeaderTop;
            }
            #endregion
        }
        #endregion

        #region Properties
        /// <summary>
        /// The statistics for all streams in the IStorage associated with this instance.
        /// </summary>
        public Dictionary<string, STATSTG> StreamStatistics = new Dictionary<string, STATSTG>();

        /// <summary>
        /// The statistics for all storgages in the IStorage associated with this instance.
        /// </summary>
        public Dictionary<string, STATSTG> SubStorageStatistics = new Dictionary<string, STATSTG>();

        /// <summary>
        /// Header size of the property stream in the IStorage associated with this instance.
        /// </summary>
        private int _propHeaderSize = Consts.PropertiesStreamHeaderTop;

        /// <summary>
        /// A reference to the parent message that this message may belong to.
        /// </summary>
        private Storage _parentMessage;

        /// <summary>
        /// The IStorage associated with this instance.
        /// </summary>
        private NativeMethods.IStorage _storage;

        /// <summary>
        /// Gets the top level outlook message from a sub message at any level.
        /// </summary>
        /// <value> The top level outlook message. </value>
        private Storage TopParent
        {
            get { return _parentMessage != null ? _parentMessage.TopParent : this; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is the top level outlook message.
        /// </summary>
        /// <value> <c>true</c> if this instance is the top level outlook message; otherwise, <c>false</c> . </value>
        private bool IsTopParent
        {
            get { return _parentMessage == null; }
        }
        #endregion

        #region Constructors & Destructor
        /// <summary>
        /// Initializes a new instance of the <see cref="Storage" /> class from a file.
        /// </summary>
        /// <param name="storageFilePath"> The file to load. </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        private Storage(string storageFilePath)
        {
            // Ensure provided file is an IStorage
            if (NativeMethods.StgIsStorageFile(storageFilePath) != 0)
                throw new ArgumentException("The provided file is not a valid IStorage", "storageFilePath");

            // Open and load IStorage from file
            NativeMethods.IStorage fileStorage;
            NativeMethods.StgOpenStorage(storageFilePath, null, NativeMethods.Stgm.Read | NativeMethods.Stgm.ShareDenyWrite, IntPtr.Zero, 0, out fileStorage);
            
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            LoadStorage(fileStorage);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Storage" /> class from a <see cref="Stream" /> containing an IStorage.
        /// </summary>
        /// <param name="storageStream"> The <see cref="Stream" /> containing an IStorage. </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        private Storage(Stream storageStream)
        {
            NativeMethods.IStorage memoryStorage = null;
            NativeMethods.ILockBytes memoryStorageBytes = null;
            try
            {
                //read stream into buffer
                var buffer = new byte[storageStream.Length];
                storageStream.Read(buffer, 0, buffer.Length);

                //create a ILockBytes (unmanaged byte array) and write buffer into it
                NativeMethods.CreateILockBytesOnHGlobal(IntPtr.Zero, true, out memoryStorageBytes);
                memoryStorageBytes.WriteAt(0, buffer, buffer.Length, null);

                //ensure provided stream data is an IStorage
                if (NativeMethods.StgIsStorageILockBytes(memoryStorageBytes) != 0)
                {
                    throw new ArgumentException("The provided stream is not a valid IStorage", "storageStream");
                }

                // Open and load IStorage on the ILockBytes
                NativeMethods.StgOpenStorageOnILockBytes(memoryStorageBytes, null, NativeMethods.Stgm.Read | NativeMethods.Stgm.ShareDenyWrite, IntPtr.Zero, 0, out memoryStorage);
                // ReSharper disable once DoNotCallOverridableMethodsInConstructor
                LoadStorage(memoryStorage);
            }
            catch
            {
                if (memoryStorage != null)
                {
                    Marshal.ReleaseComObject(memoryStorage);
                }
            }
            finally
            {
                if (memoryStorageBytes != null)
                {
                    Marshal.ReleaseComObject(memoryStorageBytes);
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Storage" /> class on the specified <see cref="NativeMethods.IStorage" />.
        /// </summary>
        /// <param name="storage"> The storage to create the <see cref="Storage" /> on. </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        private Storage(NativeMethods.IStorage storage)
        {
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            LoadStorage(storage);
        }

        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="Storage" /> is reclaimed by garbage collection.
        /// </summary>
        ~Storage()
        {
            Dispose(false);
        }
        #endregion

        #region LoadStorage
        /// <summary>
        /// Processes sub streams and storages on the specified storage.
        /// </summary>
        /// <param name="storage"> The storage to get sub streams and storages for. </param>
        protected virtual void LoadStorage(NativeMethods.IStorage storage)
        {
            _storage = storage;

            //ensures memory is released
            ReferenceManager.AddItem(storage);

            NativeMethods.IEnumSTATSTG storageElementEnum = null;
            try
            {
                //enum all elements of the storage
                storage.EnumElements(0, IntPtr.Zero, 0, out storageElementEnum);

                //iterate elements
                while (true)
                {
                    //get 1 element out of the com enumerator
                    uint elementStatCount;
                    var elementStats = new STATSTG[1];
                    storageElementEnum.Next(1, elementStats, out elementStatCount);

                    //break loop if element not retrieved
                    if (elementStatCount != 1)
                    {
                        break;
                    }

                    var elementStat = elementStats[0];
                    switch (elementStat.type)
                    {
                        case 1:
                            //element is a storage. add its statistics object to the storage dictionary
                            SubStorageStatistics.Add(elementStat.pwcsName, elementStat);
                            break;

                        case 2:
                            //element is a stream. add its statistics object to the stream dictionary
                            StreamStatistics.Add(elementStat.pwcsName, elementStat);
                            break;
                    }
                }
            }
            finally
            {
                //free memory
                if (storageElementEnum != null)
                {
                    Marshal.ReleaseComObject(storageElementEnum);
                }
            }
        }
        #endregion

        #region GetStreamBytes
        /// <summary>
        /// Gets the data in the specified stream as a byte array.
        /// </summary>
        /// <param name="streamName"> Name of the stream to get data for. </param>
        /// <returns> A byte array containg the stream data. </returns>
        private byte[] GetStreamBytes(string streamName)
        {
            // Get statistics for stream 
            var streamStatStg = StreamStatistics[streamName];

            byte[] iStreamContent;
            IStream stream = null;
            try
            {
                // Open stream from the storage
                stream = _storage.OpenStream(streamStatStg.pwcsName, IntPtr.Zero,
                    NativeMethods.Stgm.Read | NativeMethods.Stgm.ShareExclusive, 0);

                // Read the stream into a managed byte array
                iStreamContent = new byte[streamStatStg.cbSize];
                stream.Read(iStreamContent, iStreamContent.Length, IntPtr.Zero);
            }
            finally
            {
                if (stream != null)
                    Marshal.ReleaseComObject(stream);
            }

            // Return the stream bytes
            return iStreamContent;
        }
        #endregion

        #region GetStreamAsString
        /// <summary>
        /// Gets the data in the specified stream as a string using the specifed encoding to decode the stream data.
        /// </summary>
        /// <param name="streamName"> Name of the stream to get string data for. </param>
        /// <param name="streamEncoding"> The encoding to decode the stream data with. </param>
        /// <returns> The data in the specified stream as a string. </returns>
        private string GetStreamAsString(string streamName, Encoding streamEncoding)
        {
            try
            {
                var streamReader = new StreamReader(new MemoryStream(GetStreamBytes(streamName)), streamEncoding);
                var streamContent = streamReader.ReadToEnd();
                streamReader.Close();

                return streamContent;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
        #endregion

        #region GetMapiProperty
        /// <summary>
        /// Gets the raw value of the MAPI property.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The raw value of the MAPI property. </returns>
        internal object GetMapiProperty(string propIdentifier)
        {
            // Try get prop value from stream or storage
            // If not found in stream or storage try get prop value from property stream
            var propValue = GetMapiPropertyFromStreamOrStorage(propIdentifier) ??
                            GetMapiPropertyFromPropertyStream(propIdentifier);

            
            return propValue;
        }
        #endregion

        #region GetMapiPropertyFromStreamOrStorage
        /// <summary>
        /// Gets the MAPI property value from a stream or storage in this storage.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property or null if not found. </returns>
        private object GetMapiPropertyFromStreamOrStorage(string propIdentifier)
        {
            // Get list of stream and storage identifiers which map to properties
            var propKeys = new List<string>();
            propKeys.AddRange(StreamStatistics.Keys);
            propKeys.AddRange(SubStorageStatistics.Keys);

            // Determine if the property identifier is in a stream or sub storage
            string propTag = null;
            var propType = Consts.PtUnspecified;

            foreach (var propKey in propKeys)
            {
                if (!propKey.StartsWith("__substg1.0_" + propIdentifier)) continue;
                propTag = propKey.Substring(12, 8);
                propType = ushort.Parse(propKey.Substring(16, 4), NumberStyles.HexNumber);
                break;
            }

            // When null then we didn't find the property
            if (propTag == null)
                return null;

            // Depending on prop type use method to get property value
            var containerName = "__substg1.0_" + propTag;
            switch (propType)
            {
                case Consts.PtUnspecified:
                    return null;

                case Consts.PtString8:
                    //return GetStreamAsString(containerName, Encoding.UTF8);
                    return GetStreamAsString(containerName, Encoding.Default);

                case Consts.PtUnicode:
                    return GetStreamAsString(containerName, Encoding.Unicode);

                case Consts.PtBinary:
                    return GetStreamBytes(containerName);

                case Consts.PtMvUnicode:

                    // If the property is a unicode multiview item we need to read all the properties
                    // again and filter out all the multivalue names, they end with -00000000, -00000001, etc..
                    var multiValueContainerNames = propKeys.Where(propKey => propKey.StartsWith(containerName + "-")).ToList();

                    var values = new List<string>();
                    foreach (var multiValueContainerName in multiValueContainerNames)
                    {
                        var value = GetStreamAsString(multiValueContainerName, Encoding.Unicode);
                        // multi values always end with a null char so we need to strip that one off
                        if (value.EndsWith("/0"))
                            value = value.Substring(0, value.Length - 1);

                        values.Add(value);
                    }
                    
                    return values;
                    
                case Consts.PtObject:
                    return
                        NativeMethods.CloneStorage(
                            _storage.OpenStorage(containerName, IntPtr.Zero,
                                NativeMethods.Stgm.Read | NativeMethods.Stgm.ShareExclusive,
                                IntPtr.Zero, 0), true);

                default:
                    throw new ApplicationException("MAPI property has an unsupported type and can not be retrieved.");
            }
        }

        /// <summary>
        /// Gets the MAPI property value from the property stream in this storage.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property or null if not found. </returns>
        private object GetMapiPropertyFromPropertyStream(string propIdentifier)
        {
            // If no property stream return null
            if (!StreamStatistics.ContainsKey(Consts.PropertiesStream))
                return null;

            // Get the raw bytes for the property stream
            var propBytes = GetStreamBytes(Consts.PropertiesStream);

            // Iterate over property stream in 16 byte chunks starting from end of header
            for (var i = _propHeaderSize; i < propBytes.Length; i = i + 16)
            {
                // Get property type located in the 1st and 2nd bytes as a unsigned short value
                var propType = BitConverter.ToUInt16(propBytes, i);

                // Get property identifer located in 3nd and 4th bytes as a hexdecimal string
                var propIdent = new[] { propBytes[i + 3], propBytes[i + 2] };
                var propIdentString = BitConverter.ToString(propIdent).Replace("-", "");

                // If this is not the property being gotten continue to next property
                if (propIdentString != propIdentifier) continue;

                // Depending on prop type use method to get property value
                switch (propType)
                {
                    case Consts.PtI2:
                        return BitConverter.ToInt16(propBytes, i + 8);

                    case Consts.PtLong:
                        return BitConverter.ToInt32(propBytes, i + 8);

                    case Consts.PtSystime:
                        var fileTime = BitConverter.ToInt64(propBytes, i + 8);
                        return DateTime.FromFileTime(fileTime);

                    case Consts.PtBoolean:
                        return BitConverter.ToBoolean(propBytes, i + 8);

                    //default:
                    //throw new ApplicationException("MAPI property has an unsupported type and can not be retrieved.");
                }
            }

            // Property not found return null
            return null;
        }

        /// <summary>
        /// Gets the value of the MAPI property as a string.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property as a string. </returns>
        public string GetMapiPropertyString(string propIdentifier)
        {
            return GetMapiProperty(propIdentifier) as string;
        }

        /// <summary>
        /// Gets the value of the MAPI property as a short.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property as a short. </returns>
        public Int16 GetMapiPropertyInt16(string propIdentifier)
        {
            return (Int16) GetMapiProperty(propIdentifier);
        }

        /// <summary>
        /// Gets the value of the MAPI property as a integer.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property as a integer. </returns>
        public int GetMapiPropertyInt32(string propIdentifier)
        {
            return (int) GetMapiProperty(propIdentifier);
        }

        /// <summary>
        /// Gets the value of the MAPI property as a datetime.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property as a datetime or null when not set </returns>
        public DateTime? GetMapiPropertyDateTime(string propIdentifier)
        {
            var value = GetMapiProperty(propIdentifier);

            if (value != null)
                return (DateTime)GetMapiProperty(propIdentifier);

            return null;
        }

        /// <summary>
        /// Gets the value of the MAPI property as a bool.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property as a boolean or null when not set. </returns>
        public bool? GetMapiPropertyBool(string propIdentifier)
        {
            var value = GetMapiProperty(propIdentifier);

            if (value != null)
                return (bool)GetMapiProperty(propIdentifier);

            return null;
        }

        /// <summary>
        /// Gets the value of the MAPI property as a byte array.
        /// </summary>
        /// <param name="propIdentifier"> The 4 char hexadecimal prop identifier. </param>
        /// <returns> The value of the MAPI property as a byte array. </returns>
        public byte[] GetMapiPropertyBytes(string propIdentifier)
        {
            return (byte[])GetMapiProperty(propIdentifier);
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                Disposing();

            if (_storage == null) return;
            ReferenceManager.RemoveItem(_storage);
            Marshal.ReleaseComObject(_storage);
            _storage = null;
        }

        /// <summary>
        /// Gives sub classes the chance to free resources during object disposal.
        /// </summary>
        protected virtual void Disposing()
        {
        }
        #endregion
    }
}