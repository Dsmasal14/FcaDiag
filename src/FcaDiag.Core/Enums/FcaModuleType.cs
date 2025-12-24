namespace FcaDiag.Core.Enums;

/// <summary>
/// FCA vehicle module types
/// </summary>
public enum FcaModuleType
{
    Unknown = 0,

    // Powertrain
    PCM,    // Powertrain Control Module
    TCM,    // Transmission Control Module

    // Chassis
    ABS,    // Anti-lock Braking System / ESC
    EPS,    // Electric Power Steering

    // Body
    BCM,    // Body Control Module
    IPC,    // Instrument Panel Cluster
    RADIO,  // Radio/Head Unit
    HVAC,   // HVAC Control Module

    // Safety
    ACSM,   // Airbag Control Module
    OCM,    // Occupant Classification Module

    // ADAS
    ACC,    // Adaptive Cruise Control
    FCM,    // Forward Collision Module
    BSM,    // Blind Spot Monitor
    PAM,    // Park Assist Module

    // Network/Security
    SGW,    // Security Gateway
    WCM,    // Wireless Control Module
    TPMS,   // Tire Pressure Monitoring System

    // Doors
    DDM,    // Driver Door Module
    PDM     // Passenger Door Module
}
