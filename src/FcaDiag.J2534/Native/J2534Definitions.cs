namespace FcaDiag.J2534.Native;

/// <summary>
/// J2534 API error codes
/// </summary>
public enum J2534Error : uint
{
    STATUS_NOERROR = 0x00,
    ERR_NOT_SUPPORTED = 0x01,
    ERR_INVALID_CHANNEL_ID = 0x02,
    ERR_INVALID_PROTOCOL_ID = 0x03,
    ERR_NULL_PARAMETER = 0x04,
    ERR_INVALID_IOCTL_VALUE = 0x05,
    ERR_INVALID_FLAGS = 0x06,
    ERR_FAILED = 0x07,
    ERR_DEVICE_NOT_CONNECTED = 0x08,
    ERR_TIMEOUT = 0x09,
    ERR_INVALID_MSG = 0x0A,
    ERR_INVALID_TIME_INTERVAL = 0x0B,
    ERR_EXCEEDED_LIMIT = 0x0C,
    ERR_INVALID_MSG_ID = 0x0D,
    ERR_DEVICE_IN_USE = 0x0E,
    ERR_INVALID_IOCTL_ID = 0x0F,
    ERR_BUFFER_EMPTY = 0x10,
    ERR_BUFFER_FULL = 0x11,
    ERR_BUFFER_OVERFLOW = 0x12,
    ERR_PIN_INVALID = 0x13,
    ERR_CHANNEL_IN_USE = 0x14,
    ERR_MSG_PROTOCOL_ID = 0x15,
    ERR_INVALID_FILTER_ID = 0x16,
    ERR_NO_FLOW_CONTROL = 0x17,
    ERR_NOT_UNIQUE = 0x18,
    ERR_INVALID_BAUDRATE = 0x19,
    ERR_INVALID_DEVICE_ID = 0x1A
}

/// <summary>
/// J2534 protocol IDs
/// </summary>
public enum J2534Protocol : uint
{
    J1850VPW = 0x01,
    J1850PWM = 0x02,
    ISO9141 = 0x03,
    ISO14230 = 0x04,
    CAN = 0x05,
    ISO15765 = 0x06,
    SCI_A_ENGINE = 0x07,
    SCI_A_TRANS = 0x08,
    SCI_B_ENGINE = 0x09,
    SCI_B_TRANS = 0x0A
}

/// <summary>
/// J2534 connection flags
/// </summary>
[Flags]
public enum J2534ConnectFlag : uint
{
    NONE = 0x0000,
    ISO9141_NO_CHECKSUM = 0x0200,
    CAN_29BIT_ID = 0x0100,
    ISO9141_K_LINE_ONLY = 0x1000,
    CAN_ID_BOTH = 0x0800,
    ISO15765_ADDR_TYPE = 0x0080  // 29-bit for ISO15765
}

/// <summary>
/// J2534 filter types
/// </summary>
public enum J2534FilterType : uint
{
    PASS_FILTER = 0x01,
    BLOCK_FILTER = 0x02,
    FLOW_CONTROL_FILTER = 0x03
}

/// <summary>
/// J2534 IOCTL IDs
/// </summary>
public enum J2534Ioctl : uint
{
    GET_CONFIG = 0x01,
    SET_CONFIG = 0x02,
    READ_VBATT = 0x03,
    FIVE_BAUD_INIT = 0x04,
    FAST_INIT = 0x05,
    CLEAR_TX_BUFFER = 0x07,
    CLEAR_RX_BUFFER = 0x08,
    CLEAR_PERIODIC_MSGS = 0x09,
    CLEAR_MSG_FILTERS = 0x0A,
    CLEAR_FUNCT_MSG_LOOKUP_TABLE = 0x0B,
    ADD_TO_FUNCT_MSG_LOOKUP_TABLE = 0x0C,
    DELETE_FROM_FUNCT_MSG_LOOKUP_TABLE = 0x0D,
    READ_PROG_VOLTAGE = 0x0E
}

/// <summary>
/// J2534 config parameter IDs
/// </summary>
public enum J2534Parameter : uint
{
    DATA_RATE = 0x01,
    LOOPBACK = 0x03,
    NODE_ADDRESS = 0x04,
    NETWORK_LINE = 0x05,
    P1_MIN = 0x06,
    P1_MAX = 0x07,
    P2_MIN = 0x08,
    P2_MAX = 0x09,
    P3_MIN = 0x0A,
    P3_MAX = 0x0B,
    P4_MIN = 0x0C,
    P4_MAX = 0x0D,
    W1 = 0x0E,
    W2 = 0x0F,
    W3 = 0x10,
    W4 = 0x11,
    W5 = 0x12,
    TIDLE = 0x13,
    TINIL = 0x14,
    TWUP = 0x15,
    PARITY = 0x16,
    BIT_SAMPLE_POINT = 0x17,
    SYNC_JUMP_WIDTH = 0x18,
    T1_MAX = 0x1F,
    T2_MAX = 0x20,
    T4_MAX = 0x21,
    T5_MAX = 0x22,
    ISO15765_BS = 0x23,
    ISO15765_STMIN = 0x24,
    DATA_BITS = 0x25,
    FIVE_BAUD_MOD = 0x26,
    BS_TX = 0x27,
    STMIN_TX = 0x28,
    T3_MAX = 0x29,
    ISO15765_WFT_MAX = 0x2A
}

/// <summary>
/// J2534 message transmit flags
/// </summary>
[Flags]
public enum J2534TxFlag : uint
{
    NONE = 0x00000000,
    ISO15765_FRAME_PAD = 0x00000040,
    ISO15765_ADDR_TYPE = 0x00000080,  // 29-bit addressing
    CAN_29BIT_ID = 0x00000100,
    WAIT_P3_MIN_ONLY = 0x00000200,
    SW_CAN_HV_TX = 0x00000400,
    SCI_MODE = 0x00400000,
    SCI_TX_VOLTAGE = 0x00800000
}

/// <summary>
/// J2534 message receive flags
/// </summary>
[Flags]
public enum J2534RxStatus : uint
{
    NONE = 0x00000000,
    TX_MSG_TYPE = 0x00000001,        // Echoed TX message
    START_OF_MESSAGE = 0x00000002,
    ISO15765_FIRST_FRAME = 0x00000002,
    RX_BREAK = 0x00000004,
    TX_INDICATION = 0x00000008,
    ISO15765_PADDING_ERROR = 0x00000010,
    ISO15765_EXT_ADDR = 0x00000080,
    CAN_29BIT_ID = 0x00000100
}
