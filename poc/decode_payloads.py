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

def decode_proto(buf, schema=None, depth=0):
    i = 0
    indent = '  ' * depth
    while i < len(buf):
        try:
            tag, i = read_varint(buf, i)
            field = tag >> 3
            wtype = tag & 0x7
            fname = schema.get(field, {}).get('name', 'field[%d]' % field) if schema else 'field[%d]' % field
            ftype = schema.get(field, {}).get('type', None) if schema else None

            if wtype == 0:
                val, i = read_varint(buf, i)
                if ftype == 'sint32':
                    val = zigzag(val)
                print('%s%s = %s' % (indent, fname, val))
            elif wtype == 1:
                data = buf[i:i+8]; i += 8
                print('%s%s = fixed64:%s' % (indent, fname, data.hex()))
            elif wtype == 2:
                ln, i = read_varint(buf, i)
                data = buf[i:i+ln]; i += ln
                sub_schema = schema.get(field, {}).get('sub_schema') if schema else None
                try:
                    s = data.decode('utf-8')
                    if all(32 <= ord(c) < 127 or c in '\n\r\t' for c in s) and ln > 0:
                        print('%s%s = "%s"' % (indent, fname, s))
                        continue
                except:
                    pass
                try:
                    print('%s%s (sub-message, len=%d):' % (indent, fname, ln))
                    decode_proto(data, sub_schema, depth+1)
                    continue
                except:
                    pass
                print('%s%s = bytes:%s' % (indent, fname, data.hex()))
            elif wtype == 5:
                data = buf[i:i+4]; i += 4
                f32 = struct.unpack('<f', data)[0]
                u32 = struct.unpack('<I', data)[0]
                print('%s%s = fixed32:%s float=%.4f uint=%d' % (indent, fname, data.hex(), f32, u32))
            else:
                print('%s[unknown wtype %d at field %d]' % (indent, wtype, field))
                break
        except Exception as e:
            print('%s[parse error: %s]' % (indent, e))
            break

# Outer wrapper schema
outer_schema = {
    1:  {'name': 'pdata'},
    2:  {'name': 'src'},
    3:  {'name': 'dest'},
    4:  {'name': 'd_src'},
    5:  {'name': 'd_dest'},
    6:  {'name': 'enc_type'},
    7:  {'name': 'check_type'},
    8:  {'name': 'cmd_func'},
    9:  {'name': 'cmd_id'},
    10: {'name': 'data_len'},
    11: {'name': 'need_ack'},
    12: {'name': 'is_ack'},
    14: {'name': 'seq'},
    15: {'name': 'product_id'},
    16: {'name': 'version'},
    17: {'name': 'payload_ver'},
    18: {'name': 'time_snap'},
    19: {'name': 'is_rw_cmd'},
    20: {'name': 'is_queue'},
    21: {'name': 'ack_type'},
    22: {'name': 'code'},
    23: {'name': 'from'},
    24: {'name': 'module_sn'},
    25: {'name': 'device_sn'},
}

display_schema = {
    1:  {'name': 'errcode'},
    3:  {'name': 'pow_in_sum_w'},
    4:  {'name': 'pow_out_sum_w'},
    5:  {'name': 'lcd_light'},
    6:  {'name': 'energy_backup_state'},
    9:  {'name': 'pow_get_qcusb1'},
    10: {'name': 'pow_get_qcusb2'},
    11: {'name': 'pow_get_typec1'},
    12: {'name': 'pow_get_typec2'},
    17: {'name': 'dev_standby_time'},
    18: {'name': 'screen_off_time'},
    19: {'name': 'ac_standby_time'},
    20: {'name': 'dc_standby_time'},
    30: {'name': 'pcs_fan_level'},
    35: {'name': 'pow_get_pv_h'},
    36: {'name': 'pow_get_pv_l'},
    47: {'name': 'flow_info_ac_in'},
    48: {'name': 'flow_info_ac_hv_out'},
    49: {'name': 'flow_info_ac_lv_out'},
    52: {'name': 'pow_get_llc'},
    53: {'name': 'pow_get_ac'},
    54: {'name': 'pow_get_ac_in'},
    55: {'name': 'pow_get_ac_hv_out'},
    56: {'name': 'pow_get_ac_lv_out'},
    61: {'name': 'plug_in_info_ac_in_flag'},
    62: {'name': 'plug_in_info_ac_in_feq'},
}

bms_schema = {
    1:  {'name': 'num'},
    2:  {'name': 'type'},
    3:  {'name': 'cell_id'},
    4:  {'name': 'err_code'},
    5:  {'name': 'sys_ver'},
    6:  {'name': 'soc'},
    7:  {'name': 'vol_mV'},
    8:  {'name': 'amp_mA', 'type': 'sint32'},
    9:  {'name': 'temp_0p1C', 'type': 'sint32'},
    10: {'name': 'open_bms_flag'},
    11: {'name': 'design_cap'},
    12: {'name': 'remain_cap'},
    13: {'name': 'full_cap'},
    14: {'name': 'cycles'},
    15: {'name': 'soh'},
    16: {'name': 'max_cell_vol'},
    17: {'name': 'min_cell_vol'},
    18: {'name': 'max_cell_temp', 'type': 'sint32'},
    19: {'name': 'min_cell_temp', 'type': 'sint32'},
    20: {'name': 'max_mos_temp', 'type': 'sint32'},
    21: {'name': 'min_mos_temp', 'type': 'sint32'},
    22: {'name': 'bms_fault'},
    23: {'name': 'bq_sys_stat_reg'},
    24: {'name': 'tag_chg_amp'},
    25: {'name': 'f32_show_soc'},
    26: {'name': 'input_watts'},
    27: {'name': 'output_watts'},
    28: {'name': 'remain_time'},
    29: {'name': 'mos_state'},
    30: {'name': 'balance_state'},
    31: {'name': 'max_vol_diff'},
    32: {'name': 'cell_series_num'},
    33: {'name': 'cell_vol[]'},
    34: {'name': 'cell_ntc_num'},
    35: {'name': 'cell_temp[]', 'type': 'sint32'},
    36: {'name': 'hw_ver'},
    37: {'name': 'bms_heartbeat_ver'},
    38: {'name': 'ecloud_ocv'},
    39: {'name': 'bms_sn'},
    40: {'name': 'product_type'},
    41: {'name': 'product_detail'},
    42: {'name': 'act_soc'},
    43: {'name': 'diff_soc'},
    44: {'name': 'target_soc'},
    45: {'name': 'sys_loader_ver'},
    46: {'name': 'sys_state'},
    47: {'name': 'chg_dsg_state'},
    48: {'name': 'all_err_code'},
    49: {'name': 'all_bms_fault'},
    50: {'name': 'accu_chg_cap'},
    51: {'name': 'accu_dsg_cap'},
    52: {'name': 'real_soh'},
    53: {'name': 'calendar_soh'},
    54: {'name': 'cycle_soh'},
    55: {'name': 'mos_ntc_num'},
    56: {'name': 'mos_temp[]', 'type': 'sint32'},
    57: {'name': 'env_ntc_num'},
    58: {'name': 'env_temp[]', 'type': 'sint32'},
    63: {'name': 'max_env_temp', 'type': 'sint32'},
    64: {'name': 'min_env_temp', 'type': 'sint32'},
    69: {'name': 'balance_cmd'},
    70: {'name': 'remain_balance_time[]'},
    71: {'name': 'afe_sys_status'},
    72: {'name': 'mcu_pin_in_status'},
    73: {'name': 'mcu_pin_out_status'},
    74: {'name': 'bms_alarm_state1'},
    75: {'name': 'bms_alarm_state2'},
    76: {'name': 'bms_protect_state1'},
    77: {'name': 'bms_protect_state2'},
    78: {'name': 'bms_fault_state'},
    79: {'name': 'accu_chg_energy'},
    80: {'name': 'accu_dsg_energy'},
    81: {'name': 'pack_sn'},
    82: {'name': 'water_in_flag'},
}

ems_v1p0_schema = {
    1:  {'name': 'chg_state'},
    2:  {'name': 'chg_cmd'},
    3:  {'name': 'dsg_cmd'},
    4:  {'name': 'chg_vol'},
    5:  {'name': 'chg_amp'},
    6:  {'name': 'fan_level'},
    7:  {'name': 'max_charge_soc'},
    8:  {'name': 'bms_model'},
    9:  {'name': 'lcd_show_soc'},
    10: {'name': 'open_ups_flag'},
    11: {'name': 'bms_warning_state'},
    12: {'name': 'chg_remain_time'},
    13: {'name': 'dsg_remain_time'},
    14: {'name': 'ems_is_normal_flag'},
    15: {'name': 'f32_lcd_show_soc'},
    16: {'name': 'bms_is_connt[]'},
    17: {'name': 'max_available_num'},
    18: {'name': 'open_bms_idx'},
}

ems_v1p3_schema = {
    1: {'name': 'chg_disable_cond'},
    2: {'name': 'dsg_disable_cond'},
    3: {'name': 'chg_line_plug_in_flag'},
    4: {'name': 'sys_chg_dsg_state'},
    5: {'name': 'ems_heartbeat_ver'},
}

cms_schema = {
    1: {'name': 'v1p0', 'sub_schema': ems_v1p0_schema},
    2: {'name': 'v1p3', 'sub_schema': ems_v1p3_schema},
}

payloads = [
    (bytes.fromhex('0a3a0a16273a3a49791f3a3a49798f393a3a6779bf2d3a3a67f91002182020013001380340fe0148155016580170bab4ee0978819a01800103880101'),
     'DisplayPropertyUpload (cmdFunc=254, cmdId=21)', display_schema),
    (bytes.fromhex('0a3a0a1658454518066045451806f04645451d06c05245451d861002182020013001380340fe0148155016580170c5b4ee0978819a01800103880101'),
     'DisplayPropertyUpload (cmdFunc=254, cmdId=21)', display_schema),
    (bytes.fromhex('0a8e030af602080010011802200028c7808010304538fca00340daffffffffffffffff014812500158a09c0160906b68a09c01700378648001891a8801871a900114980112a00117a80115b00100b80103c001d00fcd01341f8942d00100d80102e001c815e80102f00100f801028002108a0220871a871a871a871a871a881a881a871a881a871a871a871a891a891a891a881a9002049a020414121313a2020656312e302e31a8028602b002ffff03ba021030303030303030303030303030303030c0024dc80202d50244dd7c42dd02f899b23fe502b73a7842e802ffffffff0ff00203f802008003008803009003bee8049803909302a5030000c842ad0300000000b503b1ffc742b80302c203021715c80301d2030117e80301f2030116f80317800417980416a00416a80400b2041000000000000000000000000000000000b80403c004b7a203c804c7c004d00400d80400e00400e80400f00400f80493228005920a8a05105032333150373038504835443133313890050010031820200128014020483250f602800103880101'),
     'BMSHeartBeatReport (cmdFunc=32, cmdId=50)', bms_schema),
    (bytes.fromhex('0a740a51c088c2cbdac8d2cbea0215c8e252bffacaf28c8acb828f9aca92caaa352ec2a202dfbacbb772d4438848cbc9c9cacb42cbcb5acbcb52cbca6acbca62cbc07acbc072cbf8d8c1c2cfdacad2cbeacae249c810031820200128013001380340204802505170caf7f74678829a01800103880101'),
     'CMSHeartBeatReport (cmdFunc=32, cmdId=2)', cms_schema),
]

SEP = '=' * 70
for raw, desc, inner_schema in payloads:
    print('\n' + SEP)
    print('PAYLOAD: %s  (len=%d)' % (desc, len(raw)))
    print(SEP)
    i = 0
    while i < len(raw):
        tag, i = read_varint(raw, i)
        field = tag >> 3
        wtype = tag & 0x7
        fname = outer_schema.get(field, {}).get('name', 'field[%d]' % field)
        if wtype == 0:
            val, i = read_varint(raw, i)
            print('  [HEADER] %s = %d' % (fname, val))
        elif wtype == 2:
            ln, i = read_varint(raw, i)
            data = raw[i:i+ln]; i += ln
            if field == 1:
                print('  [PAYLOAD] %s (len=%d):' % (fname, ln))
                decode_proto(data, inner_schema, depth=2)
            else:
                try:
                    s = data.decode('utf-8')
                    print('  [HEADER] %s = "%s"' % (fname, s))
                except:
                    print('  [HEADER] %s = bytes:%s' % (fname, data.hex()))
        elif wtype == 5:
            data = raw[i:i+4]; i += 4
            print('  [HEADER] %s = fixed32:%s' % (fname, data.hex()))
        else:
            print('  [unknown wtype %d at field %d]' % (wtype, field))
            break
