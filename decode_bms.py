#!/usr/bin/env python3
import struct

def read_varint(buf, i):
    shift = 0; val = 0
    while True:
        b = buf[i]; i += 1
        val |= (b & 0x7F) << shift
        if not (b & 0x80): return val, i
        shift += 7

def zigzag(n):
    return (n >> 1) ^ -(n & 1)

def decode_repeated_varints(data, signed=False):
    vals = []; i = 0
    while i < len(data):
        v, i = read_varint(data, i)
        if signed: v = zigzag(v)
        vals.append(v)
    return vals

bms_names = {
    1: ('num','u'), 2: ('type','u'), 3: ('cell_id','u'), 4: ('err_code','u'),
    5: ('sys_ver','u'), 6: ('soc_%','u'), 7: ('vol_mV','u'), 8: ('amp_mA','s'),
    9: ('temp_dC','s'), 10: ('open_bms_flag','u'), 11: ('design_cap_mAh','u'),
    12: ('remain_cap_mAh','u'), 13: ('full_cap_mAh','u'), 14: ('cycles','u'),
    15: ('soh_%','u'), 16: ('max_cell_vol_mV','u'), 17: ('min_cell_vol_mV','u'),
    18: ('max_cell_temp_dC','s'), 19: ('min_cell_temp_dC','s'),
    20: ('max_mos_temp_dC','s'), 21: ('min_mos_temp_dC','s'),
    22: ('bms_fault','u'), 23: ('bq_sys_stat_reg','u'), 24: ('tag_chg_amp_mA','u'),
    25: ('f32_show_soc','f'), 26: ('input_watts','u'), 27: ('output_watts','u'),
    28: ('remain_time_min','u'), 29: ('mos_state','u'), 30: ('balance_state','u'),
    31: ('max_vol_diff_mV','u'), 32: ('cell_series_num','u'), 33: ('cell_vol[]','rvaru'),
    34: ('cell_ntc_num','u'), 35: ('cell_temp[]','rvars'),
    36: ('hw_ver','str'), 37: ('bms_heartbeat_ver','u'), 38: ('ecloud_ocv','u'),
    39: ('bms_sn','str'), 40: ('product_type','u'), 41: ('product_detail','u'),
    42: ('act_soc','f'), 43: ('diff_soc','f'), 44: ('target_soc','f'),
    45: ('sys_loader_ver','u'), 46: ('sys_state','u'), 47: ('chg_dsg_state','u'),
    48: ('all_err_code','u'), 49: ('all_bms_fault','u'),
    50: ('accu_chg_cap_Ah','u'), 51: ('accu_dsg_cap_Ah','u'),
    52: ('real_soh','f'), 53: ('calendar_soh','f'), 54: ('cycle_soh','f'),
    55: ('mos_ntc_num','u'), 56: ('mos_temp[]','rvars'), 57: ('env_ntc_num','u'),
    58: ('env_temp[]','rvars'), 63: ('max_env_temp_dC','s'), 64: ('min_env_temp_dC','s'),
    69: ('balance_cmd','u'), 70: ('remain_balance_time[]','rvaru'),
    71: ('afe_sys_status','u'), 72: ('mcu_pin_in_status','u'), 73: ('mcu_pin_out_status','u'),
    74: ('bms_alarm_state1','u'), 75: ('bms_alarm_state2','u'),
    76: ('bms_protect_state1','u'), 77: ('bms_protect_state2','u'),
    78: ('bms_fault_state','u'), 79: ('accu_chg_energy_Wh','u'), 80: ('accu_dsg_energy_Wh','u'),
    81: ('pack_sn','str'), 82: ('water_in_flag','u'),
}

pdata = bytes.fromhex('080010011802200028c7808010304538fca00340daffffffffffffffff014812500158a09c0160906b68a09c01700378648001891a8801871a900114980112a00117a80115b00100b80103c001d00fcd01341f8942d00100d80102e001c815e80102f00100f801028002108a0220871a871a871a871a871a881a881a871a881a871a871a871a891a891a891a881a9002049a020414121313a2020656312e302e31a8028602b002ffff03ba021030303030303030303030303030303030c0024dc80202d50244dd7c42dd02f899b23fe502b73a7842e802ffffffff0ff00203f802008003008803009003bee8049803909302a5030000c842ad0300000000b503b1ffc742b80302c203021715c80301d2030117e80301f2030116f80317800417980416a00416a80400b2041000000000000000000000000000000000b80403c004b7a203c804c7c004d00400d80400e00400e80400f00400f80493228005920a8a051050323331503730385048354431333138900500')

print('=== BMSHeartBeatReport (cmdFunc=32, cmdId=50) ===')
i = 0
while i < len(pdata):
    try:
        tag, i = read_varint(pdata, i)
        field = tag >> 3; wtype = tag & 7
        info = bms_names.get(field, (str(field), 'u'))
        fname, ftype = info
        if wtype == 0:
            val, i = read_varint(pdata, i)
            if ftype == 's': val = zigzag(val)
            # Scale temperature by 0.1 if it's a temp field
            if '_dC' in fname:
                print('  %-30s = %d  (%.1f degC)' % (fname, val, val * 0.1))
            else:
                print('  %-30s = %d' % (fname, val))
        elif wtype == 2:
            ln, i = read_varint(pdata, i)
            data = pdata[i:i+ln]; i += ln
            if ftype == 'str':
                print('  %-30s = "%s"' % (fname, data.decode('utf-8', errors='replace')))
            elif ftype == 'f' and ln == 4:
                print('  %-30s = %.4f' % (fname, struct.unpack('<f', data)[0]))
            elif ftype == 'rvaru':
                vals = decode_repeated_varints(data, signed=False)
                print('  %-30s = %s' % (fname, vals))
            elif ftype == 'rvars':
                vals = decode_repeated_varints(data, signed=True)
                print('  %-30s = %s  (x0.1 degC: %s)' % (fname, vals, [v*0.1 for v in vals]))
            else:
                print('  %-30s = bytes[%d]:%s' % (fname, ln, data.hex()))
        elif wtype == 5:
            data = pdata[i:i+4]; i += 4
            print('  %-30s = %.4f (fixed32)' % (fname, struct.unpack('<f', data)[0]))
        else:
            print('  unknown wtype=%d field=%d at offset %d' % (wtype, field, i))
            break
    except Exception as e:
        print('  [error at offset %d: %s]' % (i, e))
        break
