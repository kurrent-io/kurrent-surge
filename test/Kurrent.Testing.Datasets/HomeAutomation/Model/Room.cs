// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable RedundantDefaultMemberInitializer

namespace Testing.Datasets.HomeAutomation.Model;

/// <summary>
/// Represents a room within a home
/// </summary>
public class Room {
    public Room() { }

    public Room(Guid roomId, string name, RoomType type) {
        RoomId = roomId;
        Name   = name;
        Type   = type;
    }

    public Guid     RoomId { get; set; } = Guid.Empty;
    public string   Name   { get; set; } = "";
    public RoomType Type   { get; set; } = RoomType.Unspecified;
}
