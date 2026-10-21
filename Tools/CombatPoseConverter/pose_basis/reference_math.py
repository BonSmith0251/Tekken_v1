# Mathematical reference mapping from the existing BlenderTekkenDirect research.
# Source: https://github.com/Kiloutre/BlenderTekkenDirect
import numpy as np
from math import pi, asin, atan2
halfpi=pi/2

def quaternionToRotationMatrix(Q):
    q0 = Q[0]
    q1 = Q[1]
    q2 = Q[2]
    q3 = Q[3]
    r00 = 2 * (q0 * q0 + q1 * q1) - 1
    r01 = 2 * (q1 * q2 - q0 * q3)
    r02 = 2 * (q1 * q3 + q0 * q2)
    r10 = 2 * (q1 * q2 + q0 * q3)
    r11 = 2 * (q0 * q0 + q2 * q2) - 1
    r12 = 2 * (q2 * q3 - q0 * q1)
    r20 = 2 * (q1 * q3 - q0 * q2)
    r21 = 2 * (q2 * q3 + q0 * q1)
    r22 = 2 * (q0 * q0 + q3 * q3) - 1
    rot_matrix = np.array([[r00, r01, r02], [r10, r11, r12], [r20, r21, r22]])
    return rot_matrix

def get_quaternion_from_euler(roll, pitch, yaw):
    qx = np.sin(roll / 2) * np.cos(pitch / 2) * np.cos(yaw / 2) - np.cos(roll / 2) * np.sin(pitch / 2) * np.sin(yaw / 2)
    qy = np.cos(roll / 2) * np.sin(pitch / 2) * np.cos(yaw / 2) + np.sin(roll / 2) * np.cos(pitch / 2) * np.sin(yaw / 2)
    qz = np.cos(roll / 2) * np.cos(pitch / 2) * np.sin(yaw / 2) - np.sin(roll / 2) * np.sin(pitch / 2) * np.cos(yaw / 2)
    qw = np.cos(roll / 2) * np.cos(pitch / 2) * np.cos(yaw / 2) + np.sin(roll / 2) * np.sin(pitch / 2) * np.sin(yaw / 2)
    return [qx, qy, qz, qw]

def clamp(num, min_value, max_value):
    return max(min(num, max_value), min_value)

def getRotationFromMatrix(te, mode=0):
    m11 = te[0][0]
    m12 = te[0][1]
    m13 = te[0][2]
    m21 = te[1][0]
    m22 = te[1][1]
    m23 = te[1][2]
    m31 = te[2][0]
    m32 = te[2][1]
    m33 = te[2][2]
    if mode == 0:
        y = asin(clamp(m13, -1, 1))
        if abs(m13) < 0.9999999:
            x = atan2(-m23, m33)
            z = atan2(-m12, m11)
        else:
            x = atan2(m32, m22)
            z = 0
    elif mode == 1:
        x = asin(-clamp(m23, -1, 1))
        if abs(m23) < 0.9999999:
            y = atan2(m13, m33)
            z = atan2(m21, m22)
        else:
            y = atan2(-m31, m11)
            z = 0
    elif mode == 2:
        x = asin(clamp(m32, -1, 1))
        if abs(m32) < 0.9999999:
            y = atan2(-m31, m33)
            z = atan2(-m12, m22)
        else:
            y = 0
            z = atan2(m21, m11)
    elif mode == 3:
        y = asin(-clamp(m31, -1, 1))
        if abs(m31) < 0.9999999:
            x = atan2(m32, m33)
            z = atan2(m21, m11)
        else:
            x = 0
            z = atan2(-m12, m22)
    elif mode == 4:
        z = asin(clamp(m21, -1, 1))
        if abs(m21) < 0.9999999:
            x = atan2(-m23, m22)
            y = atan2(-m31, m11)
        else:
            x = 0
            y = atan2(m13, m33)
    elif mode == 5:
        z = asin(-clamp(m12, -1, 1))
        if abs(m12) < 0.9999999:
            x = atan2(m32, m22)
            y = atan2(m13, m11)
        else:
            x = atan2(-m23, m33)
            y = 0
    return (x, y, z)

def convertArmToBlenderXYZ(x, y, z):
    orig_quat = get_quaternion_from_euler(-halfpi, halfpi, 0)
    orig_mat = quaternionToRotationMatrix(orig_quat)
    orig_mat = np.linalg.inv(orig_mat)
    quat = get_quaternion_from_euler(x, y, z)
    mat = quaternionToRotationMatrix(quat)
    mat = np.matmul(mat, orig_mat)
    x, y, z = getRotationFromMatrix(mat, mode=1)
    return (-z, y, -x)

def applyRotationFromAnimdata(armature, animdata):
    offset_bone = armature.pose.bones['BODY_SCALE__group']
    base_bone = armature.pose.bones['BASE']
    upper_body_bone = armature.pose.bones['Spine1']
    lower_body_bone = armature.pose.bones['Hip']
    neck_bone = armature.pose.bones['Neck']
    head_bone = armature.pose.bones['Head']
    right_inner_shoulder = armature.pose.bones['R_Shoulder']
    right_outer_shoulder = armature.pose.bones['R_Arm']
    right_elbow = armature.pose.bones['R_ForeArm']
    right_hand = armature.pose.bones['R_Hand']
    left_inner_shoulder = armature.pose.bones['L_Shoulder']
    left_outer_shoulder = armature.pose.bones['L_Arm']
    left_elbow = armature.pose.bones['L_ForeArm']
    left_hand = armature.pose.bones['L_Hand']
    right_hip = armature.pose.bones['R_UpLeg']
    right_knee = armature.pose.bones['R_Leg']
    right_foot = armature.pose.bones['R_Foot']
    left_hip = armature.pose.bones['L_UpLeg']
    left_knee = armature.pose.bones['L_Leg']
    left_foot = armature.pose.bones['L_Foot']
    offset_bone.location.x = animdata[3] / 1000
    offset_bone.location.y = animdata[4] / 1000 - 1.15
    offset_bone.location.z = animdata[5] / 1000
    offset_bone.location.x += animdata[0] / 1000
    offset_bone.location.z += -animdata[2] / 1000
    base_bone.rotation_euler.x = animdata[11]
    base_bone.rotation_euler.y = animdata[10]
    base_bone.rotation_euler.z = -animdata[9]
    upper_body_bone.rotation_euler.x = animdata[14] - halfpi
    upper_body_bone.rotation_euler.y = animdata[13]
    upper_body_bone.rotation_euler.z = animdata[12] * -1
    lower_body_bone.rotation_euler.x = animdata[17] + halfpi
    lower_body_bone.rotation_euler.y = animdata[16]
    lower_body_bone.rotation_euler.z = animdata[15] * -1
    neck_bone.rotation_euler.x = animdata[23]
    neck_bone.rotation_euler.y = animdata[22]
    neck_bone.rotation_euler.z = animdata[21] * -1
    head_bone.rotation_euler.x = animdata[25]
    head_bone.rotation_euler.y = animdata[26] - halfpi
    head_bone.rotation_euler.z = animdata[24] - halfpi
    x, y, z = convertArmToBlenderXYZ(animdata[27], animdata[28], animdata[29])
    right_inner_shoulder.rotation_euler.x = x
    right_inner_shoulder.rotation_euler.y = y
    right_inner_shoulder.rotation_euler.z = z
    right_outer_shoulder.rotation_euler.x = animdata[30] * -1 - halfpi
    right_outer_shoulder.rotation_euler.y = animdata[32] * -1
    right_outer_shoulder.rotation_euler.z = animdata[31] * -1
    right_elbow.rotation_euler.x = animdata[33] * -1
    right_elbow.rotation_euler.y = animdata[34]
    right_elbow.rotation_euler.z = animdata[35] * -1
    right_hand.rotation_euler.x = animdata[36] - halfpi
    right_hand.rotation_euler.y = animdata[38]
    right_hand.rotation_euler.z = animdata[37] * -1
    x, y, z = convertArmToBlenderXYZ(animdata[39], animdata[40], animdata[41])
    left_inner_shoulder.rotation_euler.x = 0 - x
    left_inner_shoulder.rotation_euler.y = y
    left_inner_shoulder.rotation_euler.z = z + pi
    left_outer_shoulder.rotation_euler.x = animdata[42] + halfpi
    left_outer_shoulder.rotation_euler.y = animdata[44] * -1
    left_outer_shoulder.rotation_euler.z = animdata[43]
    left_elbow.rotation_euler.x = animdata[45]
    left_elbow.rotation_euler.y = animdata[46]
    left_elbow.rotation_euler.z = animdata[47]
    left_hand.rotation_euler.x = animdata[48] * -1 + halfpi
    left_hand.rotation_euler.y = animdata[50] * -1
    left_hand.rotation_euler.z = animdata[49] * -1
    right_hip.rotation_euler.x = animdata[53]
    right_hip.rotation_euler.y = animdata[52]
    right_hip.rotation_euler.z = animdata[51] * -1
    right_knee.rotation_euler.x = animdata[56]
    right_knee.rotation_euler.y = animdata[55]
    right_knee.rotation_euler.z = animdata[54] * -1
    right_foot.rotation_euler.x = animdata[59]
    right_foot.rotation_euler.y = animdata[58]
    right_foot.rotation_euler.z = animdata[57] * -1
    left_hip.rotation_euler.x = animdata[62]
    left_hip.rotation_euler.y = animdata[61]
    left_hip.rotation_euler.z = animdata[60] * -1
    left_knee.rotation_euler.x = animdata[65]
    left_knee.rotation_euler.y = animdata[64]
    left_knee.rotation_euler.z = animdata[63] * -1
    left_foot.rotation_euler.x = animdata[68]
    left_foot.rotation_euler.y = animdata[67]
    left_foot.rotation_euler.z = animdata[66] * -1