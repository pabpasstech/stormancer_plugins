// MIT License
//
// Copyright (c) 2019 Stormancer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using MsgPack.Serialization;
using System;
using System.Collections.Generic;

namespace Stormancer.Server.Plugins.Friends
{
    public class MemberDto
    {
        public MemberDto()
        {
        }

        public MemberDto(MemberRecord record)
        {
            FriendId = record.FriendId;
            OwnerId = record.OwnerId;
            Roles = record.Roles;
            Status = record.Status;
            Tags = record.Tags;
            Expiration = record.Expiration;
        }

        [MessagePackMember(0)]
        public string Id
        {
            get
            {
                return OwnerId + "_" + FriendId;
            }
        }

        [MessagePackMember(1)]
        public string FriendId { get; set; }

        [MessagePackMember(2)]
        public string OwnerId { get; set; }

        [MessagePackMember(3)]
        public List<string> Roles { get; set; }

        [MessagePackMember(4)]
        public FriendInvitationStatus Status { get; set; }

        [MessagePackMember(5)]
        public List<string> Tags { get; set; } = new List<string>();


        /// <summary>
        /// Expiration of the record. Only applicable to block records.
        /// </summary>
        [MessagePackMember(6)]
        public DateTime Expiration { get; set; } = default;
    }

    public class MemberRecord
    {
        public MemberRecord()
        {
        }

        public MemberRecord(MemberDto dto)
        {
            FriendId = dto.FriendId;
            OwnerId = dto.OwnerId;
            Roles = dto.Roles;
            Status = dto.Status;
            Tags = dto.Tags;
            Expiration = dto.Expiration;
        }

        public string Id
        {
            get
            {
                return OwnerId + "_" + FriendId;
            }
        }

        public string FriendId { get; set; }

        public string OwnerId { get; set; }

        public List<string> Roles { get; set; } = new List<string>();

        public FriendInvitationStatus Status { get; set; }

        public List<string> Tags { get; set; } = new List<string>();

        public DateTime Expiration { get; set; } = default;
    }

    public class FriendListConfigRecord
    {
        public string Id { get; set; }

        /// <summary>
        /// Status as set by the user
        /// </summary>
        public FriendListStatusConfig Status { get; set; }

        /// <summary>
        /// Additional persisted custom data (status text, etc...)
        /// </summary>
        public string? CustomData { get; set; }

        public DateTime LastConnected { get; set; }
    }

    public class Friend
    {
        [MessagePackMember(0)]
        public string UserId { get; set; }

        [MessagePackMember(1), MessagePackDateTimeMember(DateTimeConversionMethod = DateTimeMemberConversionMethod.UnixEpoc)]
        public DateTimeOffset LastConnected { get; set; } = DateTimeOffset.UnixEpoch;

        [MessagePackMember(2)]
        public FriendStatus Status { get; set; } = FriendStatus.Unknow;

        [MessagePackMember(3)]
        public List<string> Tags { get; set; } = new();

        [MessagePackMember(4)]
        public Dictionary<string, string> CustomData = new();

        [MessagePackMember(5)]
        public List<string> Roles { get; set; } = new();
    }

    [MessagePackEnum(SerializationMethod = EnumSerializationMethod.ByUnderlyingValue)]
    public enum FriendInvitationStatus
    {
        Unknow = -1,
        Accepted = 0,
        InvitationSent = 1,
        WaitingAccept = 2,
        RemovedByFriend = 3,
    }

    [MessagePackEnum(SerializationMethod = EnumSerializationMethod.ByUnderlyingValue)]
    public enum FriendListStatusConfig
    {
        Unknow = -1,
        Online = 0,
        Invisible = 1,
        Away = 2
    }

    [MessagePackEnum(SerializationMethod = EnumSerializationMethod.ByUnderlyingValue)]
    public enum FriendStatus
    {
        Unknow = -1,
        Online = 0,
        Away = 1,
        Pending = 2,
        Disconnected = 3
    }
}
