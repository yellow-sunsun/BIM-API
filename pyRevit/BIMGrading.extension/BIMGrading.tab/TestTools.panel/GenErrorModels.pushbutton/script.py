# -*- coding: utf-8 -*-
"""
批次產生錯誤模型（pyRevit / IronPython 2.7）

從一個完整的「標準模型」.rvt 出發，每個變體只改一個地方（或幾個），
另存成獨立的 .rvt，並輸出一份「答案表」CSV，記錄每個檔案改了哪個構件、
預期批改工具應該抓到什麼錯誤。

改動的類別和 RevitGradingTool 匯出的類別一致：
結構柱、結構構架（梁）、牆、樓板、門、窗。
"""
from __future__ import print_function

import io
import os
import random
import re
import shutil
import tempfile
import time
import traceback

from Autodesk.Revit.DB import (
    BasicFileInfo, BuiltInCategory, BuiltInParameter, Element, ElementId,
    CategoryType, CurveElement, Dimension, ElementTransformUtils, FailureProcessingResult,
    FailureResolutionType, FailureSeverity, FamilyInstance, FilteredElementCollector,
    HostObjAttributes, IFailuresPreprocessor, TransactionStatus,
    Level, Line, LocationCurve, LocationPoint, ModelPathUtils, OpenOptions,
    DetachFromCentralOption, SaveAsOptions, Transaction, Wall, WallKind,
    WorksharingSaveAsOptions, XYZ,
)
from System.Collections.Generic import List

from pyrevit import forms, script

app = __revit__.Application  # noqa: F821  (pyRevit 內建變數)
output = script.get_output()

MM = 1.0 / 304.8          # mm -> Revit 內部單位（英尺）
SEED = 20261124           # 固定亂數種子：同一個模型每次產生的結果都一樣

CAT_COL = BuiltInCategory.OST_StructuralColumns
CAT_BEAM = BuiltInCategory.OST_StructuralFraming
CAT_WALL = BuiltInCategory.OST_Walls
CAT_FLOOR = BuiltInCategory.OST_Floors
CAT_DOOR = BuiltInCategory.OST_Doors
CAT_WIN = BuiltInCategory.OST_Windows
ALL_CATS = [CAT_COL, CAT_BEAM, CAT_WALL, CAT_FLOOR, CAT_DOOR, CAT_WIN]


# ---------------------------------------------------------------------------
# 工具函式
# ---------------------------------------------------------------------------

class SkipVariant(Exception):
    """模型裡找不到可以改的構件時跳過這個變體。"""
    pass


# Revit 錯誤可接受的自動處理方式：只會解開接合 / 約束，不會刪除或移動其他構件，
# 確保「每個模型只錯一點」不被破壞（刻意不包含 DeleteElements、MoveElements）
SAFE_RESOLUTIONS = [getattr(FailureResolutionType, n) for n in
                    ("UnlockConstraints", "DetachElements", "SkipElements", "FixElements")
                    if hasattr(FailureResolutionType, n)]


class SwallowWarnings(IFailuresPreprocessor):
    """批次執行時不要跳出視窗：
    - 警告（例如「構件重疊」）直接略過
    - 錯誤若有安全的處理方式（解除約束、分離接合）就自動處理
    - 其他錯誤整筆回復，並記下錯誤訊息給使用者看"""

    def __init__(self):
        self.errors = []      # Revit 的錯誤訊息
        self.resolved = []    # 自動處理過的錯誤
        self.rounds = 0

    def PreprocessFailures(self, accessor):
        resolved_any = False
        for f in accessor.GetFailureMessages():
            if f.GetSeverity() == FailureSeverity.Warning:
                accessor.DeleteWarning(f)
                continue
            text = f.GetDescriptionText()
            rtype = next((t for t in SAFE_RESOLUTIONS if f.HasResolutionOfType(t)), None)
            if rtype is None or self.rounds >= 3:
                self.errors.append(text)
                return FailureProcessingResult.ProceedWithRollBack
            f.SetCurrentResolutionType(rtype)
            accessor.ResolveFailure(f)
            self.resolved.append(u"%s → %s" % (text, rtype))
            resolved_any = True
        if resolved_any:
            self.rounds += 1
            return FailureProcessingResult.ProceedWithCommit
        return FailureProcessingResult.Continue


def elem_name(e):
    """取得元素名稱。IronPython 在 FamilySymbol 等類型上直接用 .Name 會出現
    AttributeError: Name，所以改從 Element 基底類別讀，再不行就讀類型名稱參數。"""
    if e is None:
        return u""
    try:
        return e.Name
    except AttributeError:
        pass
    try:
        return Element.Name.__get__(e)
    except Exception:
        pass
    p = e.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM) or \
        e.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)
    return p.AsString() if p is not None and p.HasValue else u""


def eid(el):
    i = el.Id
    return i.Value if hasattr(i, "Value") else i.IntegerValue


def level_name(doc, el):
    lid = getattr(el, "LevelId", None)
    if lid is None or lid == ElementId.InvalidElementId:
        for bip in (BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                    BuiltInParameter.FAMILY_LEVEL_PARAM,
                    BuiltInParameter.WALL_BASE_CONSTRAINT,
                    BuiltInParameter.LEVEL_PARAM):
            p = el.get_Parameter(bip)
            if p is not None and p.HasValue:
                lid = p.AsElementId()
                break
    lv = doc.GetElement(lid) if lid is not None else None
    return elem_name(lv)


def is_in_place(el):
    if not isinstance(el, FamilyInstance):
        return False
    try:
        return el.Symbol.Family.IsInPlace
    except Exception:
        return False


def location_mm(el):
    loc = el.Location
    if is_in_place(el):
        # 內建族群的定位點固定在專案原點，跟實際位置無關，改用外框中心
        bb = el.get_BoundingBox(None)
        if bb is not None:
            p = (bb.Min + bb.Max) / 2.0
            return (p.X / MM, p.Y / MM, p.Z / MM)
    if isinstance(loc, LocationPoint):
        p = loc.Point
    elif isinstance(loc, LocationCurve):
        c = loc.Curve
        p = (c.GetEndPoint(0) + c.GetEndPoint(1)) / 2.0
    else:
        bb = el.get_BoundingBox(None)
        if bb is None:
            return (0.0, 0.0, 0.0)
        p = (bb.Min + bb.Max) / 2.0
    return (p.X / MM, p.Y / MM, p.Z / MM)


def type_name(doc, el):
    t = doc.GetElement(el.GetTypeId())
    return elem_name(t)


def collect(doc, cat):
    """收集可修改的構件：不在群組內、沒有被釘選，依 Id 排序確保結果可重現。"""
    els = [e for e in FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType()
           if e.GroupId == ElementId.InvalidElementId and not e.Pinned]
    els.sort(key=eid)
    return els


def is_vertical_column(el):
    return isinstance(el, FamilyInstance) and isinstance(el.Location, LocationPoint)


def is_line_beam(el):
    return isinstance(el.Location, LocationCurve) and isinstance(el.Location.Curve, Line)


def is_basic_line_wall(el):
    return (isinstance(el, Wall) and el.WallType.Kind == WallKind.Basic
            and isinstance(el.Location, LocationCurve) and isinstance(el.Location.Curve, Line))


def has_no_inserts(wall):
    return wall.FindInserts(True, True, True, True).Count == 0


def is_hosted_in_wall(el):
    return isinstance(el, FamilyInstance) and isinstance(el.Host, Wall)


CANDIDATES = {
    "col": (CAT_COL, is_vertical_column),
    "beam": (CAT_BEAM, is_line_beam),
    "wall": (CAT_WALL, is_basic_line_wall),
    "wall_free": (CAT_WALL, lambda e: is_basic_line_wall(e) and has_no_inserts(e)),
    "floor": (CAT_FLOOR, lambda e: True),
    "door": (CAT_DOOR, is_hosted_in_wall),
    "win": (CAT_WIN, is_hosted_in_wall),
}

CAT_LABEL = {"col": u"結構柱", "beam": u"結構構架", "wall": u"牆", "wall_free": u"牆",
             "floor": u"樓板", "door": u"門", "win": u"窗"}


def pick(doc, rng, kind):
    cat, ok = CANDIDATES[kind]
    els = [e for e in collect(doc, cat) if ok(e)]
    if not els:
        raise SkipVariant(u"模型中沒有可用的%s" % CAT_LABEL[kind])
    return rng.choice(els)


def set_param_mm(el, bip, delta_mm):
    p = el.get_Parameter(bip)
    if p is None or p.IsReadOnly:
        raise SkipVariant(u"參數 %s 無法修改" % bip)
    p.Set(p.AsDouble() + delta_mm * MM)


def perp_dir(el):
    c = el.Location.Curve
    d = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize()
    return XYZ(-d.Y, d.X, 0)


def along_host_dir(el):
    c = el.Host.Location.Curve
    return (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize()


# ---------------------------------------------------------------------------
# 錯誤種類：每個函式改一個構件，回傳一筆記錄（給答案表用）
# ---------------------------------------------------------------------------

def record(doc, kind, el, detail, expect):
    x, y, z = location_mm(el)
    return {
        "cat": CAT_LABEL[kind], "id": eid(el), "level": level_name(doc, el),
        "x": x, "y": y, "z": z, "detail": detail, "expect": expect,
    }


def op_delete(kind):
    def run(doc, rng):
        el = pick(doc, rng, kind)
        r = record(doc, kind, el, u"刪除（類型 %s）" % type_name(doc, el), u"缺少 1")
        doc.Delete(el.Id)
        return [r]
    return run


def op_copy(kind, dx_mm, dy_mm):
    def run(doc, rng):
        el = pick(doc, rng, kind)
        if dx_mm == 0 and dy_mm == 0:
            detail = u"在同一位置重複建立一個"
        else:
            detail = u"複製一個到 (+%d, +%d) mm" % (dx_mm, dy_mm)
        r = record(doc, kind, el, detail, u"多餘 1")
        ElementTransformUtils.CopyElement(doc, el.Id, XYZ(dx_mm * MM, dy_mm * MM, 0))
        return [r]
    return run


def op_move(kind, mm, how, expect=u"位置偏移 1"):
    """how: 'x' 沿 X 軸、'perp' 垂直於構件方向、'along_host' 沿著所在牆。"""
    def run(doc, rng):
        el = pick(doc, rng, kind)
        if how == "perp":
            v = perp_dir(el)
        elif how == "along_host":
            v = along_host_dir(el)
        else:
            v = XYZ(1, 0, 0)
        r = record(doc, kind, el, u"平移 %d mm（%s）" % (mm, how), expect)
        ElementTransformUtils.MoveElement(doc, el.Id, v * (mm * MM))
        return [r]
    return run


def other_types(doc, el):
    """可以換的其他類型（名稱不同）。先用 GetValidTypes，沒有的話找同類別、同種類的類型。"""
    cur_type = doc.GetElement(el.GetTypeId())
    cur_name = elem_name(cur_type)
    ids = [i for i in el.GetValidTypes() if i != cur_type.Id]
    if not ids and cur_type.Category is not None:
        is_found = getattr(cur_type, "IsFoundationSlab", None)
        ids = [t.Id for t in FilteredElementCollector(doc).OfClass(cur_type.GetType())
               if t.Id != cur_type.Id and t.Category is not None
               and t.Category.Id == cur_type.Category.Id
               and getattr(t, "IsFoundationSlab", None) == is_found]
    ids = [i for i in ids if elem_name(doc.GetElement(i)) != cur_name]
    ids.sort(key=lambda i: elem_name(doc.GetElement(i)))
    return ids


def make_thicker_type(doc, cur_type, delta_mm):
    """模型只有一種樓板/牆類型時：複製一個類型，把最厚的那層加厚，模擬「厚度選錯」。"""
    cs = cur_type.GetCompoundStructure()
    if cs is None or cs.LayerCount == 0:
        return None
    idx = max(range(cs.LayerCount), key=lambda k: cs.GetLayerWidth(k))
    new_mm = int(round((cs.GetWidth() / MM) + delta_mm))
    base = u"%s 錯誤厚度%dmm" % (elem_name(cur_type), new_mm)
    existing = set(elem_name(t) for t in FilteredElementCollector(doc).OfClass(cur_type.GetType()))
    name, n = base, 2
    while name in existing:
        name, n = u"%s(%d)" % (base, n), n + 1
    new_type = cur_type.Duplicate(name)
    cs2 = new_type.GetCompoundStructure()
    cs2.SetLayerWidth(idx, cs2.GetLayerWidth(idx) + delta_mm * MM)
    new_type.SetCompoundStructure(cs2)
    return new_type.Id


def op_change_type(kind):
    def run(doc, rng):
        el = pick(doc, rng, kind)
        cur_type = doc.GetElement(el.GetTypeId())
        valid = other_types(doc, el)
        if valid:
            new_id = rng.choice(valid)
            note = u""
        elif isinstance(cur_type, HostObjAttributes):
            new_id = make_thicker_type(doc, cur_type, 50)
            note = u"（模型只有這一種類型，複製一個加厚 50mm 的類型）"
            if new_id is None:
                raise SkipVariant(u"%s 沒有其他可換的類型，也無法複製加厚" % CAT_LABEL[kind])
        else:
            raise SkipVariant(u"%s 沒有其他可換的類型" % CAT_LABEL[kind])
        new_type = doc.GetElement(new_id)
        if hasattr(new_type, "IsActive") and not new_type.IsActive:
            new_type.Activate()
            doc.Regenerate()
        r = record(doc, kind, el, u"類型「%s」改成「%s」%s" % (elem_name(cur_type), elem_name(new_type), note),
                   u"屬性錯誤 1")
        el.ChangeTypeId(new_id)
        return [r]
    return run


def op_rotate_column(deg):
    def run(doc, rng):
        el = pick(doc, rng, "col")
        p = el.Location.Point
        axis = Line.CreateBound(p, p + XYZ.BasisZ)
        r = record(doc, "col", el, u"繞柱心旋轉 %d 度" % deg, u"旋轉錯誤 1")
        ElementTransformUtils.RotateElement(doc, el.Id, axis, deg * 3.141592653589793 / 180.0)
        return [r]
    return run


def op_column_top(delta_mm):
    def run(doc, rng):
        el = pick(doc, rng, "col")
        r = record(doc, "col", el, u"柱頂偏移 +%d mm（柱太高）" % delta_mm,
                   u"位置偏移 1（柱中心 Z 改變）")
        set_param_mm(el, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, delta_mm)
        return [r]
    return run


def op_beam_elevation(delta_mm):
    def run(doc, rng):
        el = pick(doc, rng, "beam")
        r = record(doc, "beam", el, u"起點/終點標高偏移 %d mm（梁放錯高度）" % delta_mm, u"位置偏移 1")
        set_param_mm(el, BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION, delta_mm)
        set_param_mm(el, BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION, delta_mm)
        return [r]
    return run


def op_wall_height(delta_mm):
    def run(doc, rng):
        el = pick(doc, rng, "wall")
        top = el.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)
        unconnected = top is None or top.AsElementId() == ElementId.InvalidElementId
        bip = BuiltInParameter.WALL_USER_HEIGHT_PARAM if unconnected else BuiltInParameter.WALL_TOP_OFFSET
        r = record(doc, "wall", el, u"牆高 +%d mm" % delta_mm,
                   u"可能抓不到（目前只比對牆底中心位置與類型）")
        set_param_mm(el, bip, delta_mm)
        return [r]
    return run


def op_sill(kind, delta_mm):
    def run(doc, rng):
        el = pick(doc, rng, kind)
        r = record(doc, kind, el, u"窗台/門檻高度 +%d mm" % delta_mm, u"位置偏移 1")
        set_param_mm(el, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM, delta_mm)
        return [r]
    return run


def op_flip_door(doc, rng):
    cat, ok = CANDIDATES["door"]
    els = [e for e in collect(doc, cat) if ok(e) and e.CanFlipFacing]
    if not els:
        raise SkipVariant(u"沒有可以翻轉的門")
    el = rng.choice(els)
    r = record(doc, "door", el, u"門的開啟方向翻轉（flipFacing）",
               u"可能抓不到（取決於翻轉後旋轉角是否改變）")
    el.flipFacing()
    return [r]


def moves_with_host(e):
    """門窗、帷幕嵌板、面型族群等依附在主體（牆、樓板…）上，會跟著主體移動，不能另外移。
    注意：柱、梁、家具這類「樓層型」族群的 Host 會回傳樓層（Level），它們不會跟著動，
    一定要自己移——之前把它們當成依附構件跳過，結果整棟平移時柱子都沒動。"""
    # FamilyInstance、Opening（樓板/牆上的開口）都有 Host 屬性
    try:
        h = getattr(e, "Host", None)
    except Exception:
        h = None
    if h is not None and not isinstance(h, Level):
        return True
    host_id = getattr(e, "HostId", None)   # 例如樓梯上的欄杆
    return host_id is not None and host_id != ElementId.InvalidElementId


def movable_model_elements(doc):
    """模擬在 Revit 裡「全選 → 移動」：所有模型構件一起移，避免只移一部分時
    與沒移的構件之間的約束或接合出錯。
    之前會跳過「沒有定位資訊」的元素，結果豎井開口沒移動，整棟移完後它切到樓板的
    不同位置，樓板形狀改變就被判錯；現在不再跳過，真的不能移的交給 move_robust 剔除。"""
    ids = List[ElementId]()
    for e in FilteredElementCollector(doc).WhereElementIsNotElementType():
        cat = e.Category
        if cat is None or cat.CategoryType != CategoryType.Model:
            continue
        if e.ViewSpecific or e.GroupId != ElementId.InvalidElementId:   # 群組成員跟著群組動
            continue
        if isinstance(e, CurveElement):   # 草圖線等跟著主體走
            continue
        if type(e).__name__ in ("BasePoint", "InternalOrigin"):   # 專案基準點、測量點不要動
            continue
        if moves_with_host(e):
            continue
        ids.Add(e.Id)
    return ids


def unlock_constraints(doc):
    """解開所有鎖定的尺寸與對齊約束（只用在測試複本上，約束不會被批改）。回傳解開的數量。"""
    n = 0
    for d in FilteredElementCollector(doc).OfClass(Dimension):
        try:
            if d.NumberOfSegments > 0:
                for s in d.Segments:
                    if s.IsLocked:
                        s.IsLocked = False
                        n += 1
            elif d.IsLocked:
                d.IsLocked = False
                n += 1
        except Exception:
            pass
    return n


def move_robust(doc, ids, vec, skipped):
    """一次移動一批；整批失敗時對半拆開重試，只剔除真正不能移動的元素（記在 skipped）。"""
    try:
        ElementTransformUtils.MoveElements(doc, ids, vec)
        return
    except Exception:
        if ids.Count == 1:
            skipped.append(ids[0])
            return
    half = ids.Count // 2
    a, b = List[ElementId](), List[ElementId]()
    for i in range(ids.Count):
        (a if i < half else b).Add(ids[i])
    move_robust(doc, a, vec, skipped)
    move_robust(doc, b, vec, skipped)


def describe(doc, k):
    """元件 ID → 「類別 / 類型名稱 (ID)」，給使用者看哪些沒移好。"""
    e = doc.GetElement(ElementId(k))
    if e is None:
        return unicode(k)  # noqa: F821
    cat = e.Category.Name if e.Category is not None else u"?"
    return u"%s / %s (%s)" % (cat, type_name(doc, e), k)


def compared_positions(doc):
    """批改會比對的 6 個類別：每個構件目前的位置（mm）。"""
    pos = {}
    for cat in ALL_CATS:
        for e in FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType():
            pos[eid(e)] = location_mm(e)
    return pos


def op_shift_all(dx_mm, dy_mm):
    def run(doc, rng):
        before = compared_positions(doc)
        ids = movable_model_elements(doc)
        if ids.Count == 0:
            raise SkipVariant(u"模型中沒有構件")
        notes = []

        # 被釘選的構件不能移動：先解除釘選，移完再釘回去
        pinned = []
        for i in ids:
            e = doc.GetElement(i)
            if e.Pinned:
                try:
                    e.Pinned = False
                    pinned.append(e)
                except Exception:
                    pass

        # 鎖住的尺寸 / 對齊（小鎖頭）會在移動時把構件再拖一次，造成移動兩倍
        # （例如樓板邊緣鎖在牆上：樓板自己移 3000，又被牆拖 3000）。這只是測試用的複本，先解鎖
        unlocked = unlock_constraints(doc)
        if unlocked:
            notes.append(u"解除 %d 個鎖定約束" % unlocked)

        vec = XYZ(dx_mm * MM, dy_mm * MM, 0)
        skipped = []
        move_robust(doc, ids, vec, skipped)
        if skipped:
            print(u"無法移動的元素（已略過）：" + u"；".join(describe(doc, eid(doc.GetElement(i))) for i in skipped[:20]))
            notes.append(u"略過 %d 個不能移動的元素" % len(skipped))
        doc.Regenerate()
        for e in pinned:
            try:
                e.Pinned = True
            except Exception:
                pass

        # 檢查：每個比對構件都應該剛好平移 (dx, dy, 0)
        def not_moved():
            after = compared_positions(doc)
            bad = []
            for k, (x0, y0, z0) in before.items():
                if k not in after:
                    continue
                x1, y1, z1 = after[k]
                if abs(x1 - x0 - dx_mm) > 5 or abs(y1 - y0 - dy_mm) > 5 or abs(z1 - z0) > 5:
                    bad.append((k, x1 - x0, y1 - y0, z1 - z0))
            return bad

        # 沒移好的（沒動、移兩倍、被接合拉偏）逐一補正差額，最多 3 輪
        bad = not_moved()
        fixed = set()
        for _ in range(3):
            if not bad:
                break
            for k, mx, my, mz in bad:
                e = doc.GetElement(ElementId(k))
                if e is None or moves_with_host(e) or e.GroupId != ElementId.InvalidElementId:
                    continue
                was_pinned = e.Pinned
                try:
                    if was_pinned:
                        e.Pinned = False
                    ElementTransformUtils.MoveElement(
                        doc, e.Id, XYZ((dx_mm - mx) * MM, (dy_mm - my) * MM, -mz * MM))
                    fixed.add(k)
                except Exception:
                    pass
                finally:
                    if was_pinned:
                        e.Pinned = True
            doc.Regenerate()
            bad = not_moved()
        if fixed:
            notes.append(u"補正 %d 個移動量不對的構件" % len(fixed))

        if bad:
            notes.append(u"⚠ 仍有 %d 個比對構件沒有正確平移（元件ID：%s）" % (
                len(bad), u", ".join(unicode(k) for k, _, _, _ in bad[:10])))  # noqa: F821
            # 詳細列出（類別 / 類型、實際移動量），方便找出原因
            print(u"沒有正確平移的比對構件：")
            for k, mx, my, mz in bad:
                print(u"  %s：實際移動 (%.0f, %.0f, %.0f) mm，應為 (%d, %d, 0)" % (describe(doc, k), mx, my, mz, dx_mm, dy_mm))
        expect = u"0 個錯誤（偏移校正後應完全吻合）" if not bad else \
                 u"應為 0，但有 %d 個構件沒正確平移，可能出現對應數量的錯誤" % len(bad)
        return [{"cat": u"全部", "id": u"", "level": u"", "x": 0, "y": 0, "z": 0,
                 "detail": u"整棟模型平移 (+%d, +%d) mm，共 %d 個構件%s" % (
                     dx_mm, dy_mm, ids.Count, u"".join(u"（%s）" % n for n in notes)),
                 "expect": expect}]
    return run


def op_combo(*ops):
    def run(doc, rng):
        recs = []
        for op in ops:
            recs.extend(op(doc, rng))
            doc.Regenerate()
        return recs
    return run


def op_nothing(doc, rng):
    return [{"cat": u"", "id": u"", "level": u"", "x": 0, "y": 0, "z": 0,
             "detail": u"沒有改動（對照組）", "expect": u"0 個錯誤"}]


VARIANTS = [
    (u"00_沒改變", op_nothing),
    (u"01_少一根柱", op_delete("col")),
    (u"02_多一根柱", op_copy("col", 2000, 0)),
    (u"03_重複柱_同位置", op_copy("col", 0, 0)),
    (u"04_柱位置偏移300", op_move("col", 300, "x")),
    (u"05_柱尺寸錯", op_change_type("col")),
    (u"06_柱旋轉90度", op_rotate_column(90)),
    (u"07_柱高度錯", op_column_top(500)),
    (u"08_少一根梁", op_delete("beam")),
    (u"09_梁尺寸錯", op_change_type("beam")),
    (u"10_梁位置偏移300", op_move("beam", 300, "perp")),
    (u"11_梁標高錯", op_beam_elevation(-300)),
    (u"12_少一面牆", op_delete("wall_free")),
    (u"13_牆類型錯", op_change_type("wall")),
    (u"14_牆位置偏移600", op_move("wall_free", 600, "perp")),
    (u"15_牆高度錯", op_wall_height(1000)),
    (u"16_少一塊樓板", op_delete("floor")),
    (u"17_樓板類型錯", op_change_type("floor")),
    (u"18_少一扇門", op_delete("door")),
    (u"19_門類型錯", op_change_type("door")),
    (u"20_門位置偏移500", op_move("door", 500, "along_host")),
    (u"21_門開向相反", op_flip_door),
    (u"22_少一扇窗", op_delete("win")),
    (u"23_窗類型錯", op_change_type("win")),
    (u"24_窗位置偏移500", op_move("win", 500, "along_host")),
    (u"25_窗台高度錯", op_sill("win", 300)),
    (u"26_整棟模型偏移", op_shift_all(5000, 3000)),
    (u"27_錯一堆", op_combo(op_delete("col"), op_change_type("beam"),
                          op_delete("win"), op_move("wall_free", 600, "perp"))),
]


# ---------------------------------------------------------------------------
# 開檔 / 存檔
# ---------------------------------------------------------------------------

def remove_with_backups(dst):
    """刪掉輸出檔以及 Revit 的備份檔（xxx.0001.rvt、xxx.0002.rvt…）。"""
    folder = os.path.dirname(dst)
    stem = os.path.splitext(os.path.basename(dst))[0]
    backup = re.compile(re.escape(stem) + r"\.\d{4}\.rvt$", re.IGNORECASE | re.UNICODE)
    for fn in os.listdir(folder):
        if fn == os.path.basename(dst) or backup.match(fn):
            try:
                os.remove(os.path.join(folder, fn))
            except OSError:
                pass


def open_variant(open_src, workshared):
    """非工作共用：開暫存複本（main 先複製好，原檔不會被改到）。
    工作共用：從原檔分離開啟。"""
    if workshared:
        opts = OpenOptions()
        opts.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
        return app.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(open_src), opts)
    return app.OpenDocumentFile(open_src)


def save_variant(doc, dst, workshared):
    """一律另存成新檔：存到原本不存在的路徑時 Revit 不會產生 .0001 備份。"""
    remove_with_backups(dst)
    opts = SaveAsOptions()
    opts.OverwriteExistingFile = True
    opts.MaximumBackups = 1
    if workshared:
        ws = WorksharingSaveAsOptions()
        ws.SaveAsCentral = True
        opts.SetWorksharingOptions(ws)
    doc.SaveAs(dst, opts)


MAX_TRIES = 5   # Revit 拒絕修改時，換別的構件再試幾次


def try_once(doc, name, op, rng):
    """在一個交易裡做一次修改。成功回傳記錄，失敗回傳 (None, 錯誤訊息)；文件會回到原狀。"""
    t = Transaction(doc, u"產生錯誤：" + name)
    pre = SwallowWarnings()
    fho = t.GetFailureHandlingOptions()
    fho.SetFailuresPreprocessor(pre)
    t.SetFailureHandlingOptions(fho)
    t.Start()
    try:
        recs = op(doc, rng)
    except SkipVariant:
        t.RollBack()
        raise
    except Exception as ex:
        t.RollBack()
        return None, unicode(ex)  # noqa: F821
    if t.Commit() != TransactionStatus.Committed:
        return None, u"；".join(pre.errors) or u"交易被回復"
    if pre.resolved:
        # 同樣的訊息合併計數，例如「無法保持接合元素 → DetachElements ×20」
        counts = []
        for msg in pre.resolved:
            for c in counts:
                if c[0] == msg:
                    c[1] += 1
                    break
            else:
                counts.append([msg, 1])
        recs[0]["detail"] += u"（Revit 自動處理：%s）" % u"；".join(
            m if n == 1 else u"%s ×%d" % (m, n) for m, n in counts)
    return recs, None


def run_variant(open_src, dst, name, op, workshared, rng):
    doc = open_variant(open_src, workshared)
    try:
        errors = []
        for _ in range(MAX_TRIES):
            recs, err = try_once(doc, name, op, rng)   # rng 會前進，下次會挑到別的構件
            if recs is not None:
                save_variant(doc, dst, workshared)
                return recs
            if err not in errors:
                errors.append(err)
        raise SkipVariant(u"Revit 拒絕這個修改（試了 %d 次）：%s" % (MAX_TRIES, u" / ".join(errors)))
    finally:
        doc.Close(False)
        if not workshared:
            # 保險：萬一還是產生了備份檔就刪掉，只留下 dst 本身
            stem = os.path.splitext(dst)[0]
            for n in range(1, 10):
                bk = u"%s.%04d.rvt" % (stem, n)
                if os.path.exists(bk):
                    os.remove(bk)


def csv_cell(v):
    s = v if isinstance(v, unicode) else unicode(v)  # noqa: F821 (IronPython 2.7)
    if any(ch in s for ch in u',"\n'):
        s = u'"' + s.replace(u'"', u'""') + u'"'
    return s


def write_manifest(path, rows):
    header = [u"檔名", u"錯誤說明", u"類別", u"元件ID", u"樓層", u"原X(mm)", u"原Y(mm)", u"原Z(mm)",
              u"改動細節", u"預期批改結果"]
    with io.open(path, "w", encoding="utf-8-sig") as f:
        f.write(u",".join(header) + u"\n")
        for r in rows:
            f.write(u",".join(csv_cell(c) for c in r) + u"\n")


# ---------------------------------------------------------------------------
# 主程式
# ---------------------------------------------------------------------------

def main():
    src = forms.pick_file(file_ext="rvt", title=u"選擇標準模型（完整、正確的 .rvt）")
    if not src:
        return
    default_out = os.path.join(os.path.dirname(src), u"錯誤模型")
    out_dir = forms.pick_folder(title=u"選擇輸出資料夾（取消則存到：%s）" % default_out) or default_out
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    names = [n for n, _ in VARIANTS]
    chosen = forms.SelectFromList.show(names, title=u"要產生哪些錯誤模型？",
                                       multiselect=True, button_name=u"開始產生")
    if not chosen:
        return
    todo = [(n, op) for n, op in VARIANTS if n in chosen]

    open_paths = [d.PathName for d in app.Documents]
    workshared = BasicFileInfo.Extract(src).IsWorkshared
    if workshared and any(os.path.normcase(p) == os.path.normcase(src) for p in open_paths):
        forms.alert(u"標準模型是工作共用檔，而且目前已經開著。\n請先關閉它（或開別的檔案）再執行。")
        return

    base = os.path.splitext(os.path.basename(src))[0]
    rows, skipped = [], []

    # 非工作共用：先把標準模型複製到暫存資料夾，每個變體都開這份複本、另存成新檔，
    # 複本本身從不存檔，所以原檔和複本都不會被改到，也不會產生備份檔
    if workshared:
        open_src, tmp_src = src, None
    else:
        tmp_src = os.path.join(tempfile.gettempdir(), u"bimgrading_src_%d.rvt" % int(time.time()))
        shutil.copyfile(src, tmp_src)
        open_src = tmp_src

    try:
        generate(todo, open_src, out_dir, base, workshared, rows, skipped)
    finally:
        if tmp_src is not None:
            remove_with_backups(tmp_src)

    manifest = os.path.join(out_dir, u"%s 錯誤模型答案表.csv" % base)
    write_manifest(manifest, rows)

    msg = u"完成：產生 %d 個模型\n輸出資料夾：%s\n答案表：%s" % (
        len(set(r[0] for r in rows)), out_dir, os.path.basename(manifest))
    if skipped:
        msg += u"\n\n跳過 %d 個：\n" % len(skipped) + u"\n".join(u"・%s：%s" % s for s in skipped)
    forms.alert(msg)


def generate(todo, open_src, out_dir, base, workshared, rows, skipped):
    with forms.ProgressBar(title=u"產生錯誤模型 {value}/{max_value}", cancellable=True) as pb:
        for i, (name, op) in enumerate(todo):
            if pb.cancelled:
                break
            pb.update_progress(i, len(todo))
            # 每個變體用各自的種子：單獨重跑某一個也會得到同樣結果
            rng = random.Random(SEED + VARIANTS.index((name, op)))
            dst = os.path.join(out_dir, u"%s %s.rvt" % (base, name))
            try:
                recs = run_variant(open_src, dst, name, op, workshared, rng)
                for r in recs:
                    rows.append([os.path.basename(dst), name, r["cat"], r["id"], r["level"],
                                 u"%.0f" % r["x"], u"%.0f" % r["y"], u"%.0f" % r["z"],
                                 r["detail"], r["expect"]])
                output.print_md(u"✅ **%s** — %s" % (name, u"；".join(r["detail"] for r in recs)))
            except SkipVariant as ex:
                skipped.append((name, unicode(ex)))  # noqa: F821
                output.print_md(u"⏭️ **%s** 跳過：%s" % (name, ex))
                remove_with_backups(dst)   # 別留下上次產生、與答案表不符的舊檔
            except Exception as ex:
                skipped.append((name, unicode(ex)))  # noqa: F821
                output.print_md(u"❌ **%s** 失敗：%s" % (name, ex))
                print(traceback.format_exc())
                remove_with_backups(dst)
        pb.update_progress(len(todo), len(todo))


main()
