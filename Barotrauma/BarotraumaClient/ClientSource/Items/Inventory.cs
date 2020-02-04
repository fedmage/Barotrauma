﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class InventorySlot
    {
        public Rectangle Rect;

        public Rectangle InteractRect;

        public bool Disabled;

        public GUIComponent.ComponentState State;

        public Vector2 DrawOffset;

        public Color Color;

        public Color HighlightColor;
        public float HighlightScaleUpAmount;
        private CoroutineHandle highlightCoroutine;
        public float HighlightTimer;

        public Sprite SlotSprite;

        public Keys QuickUseKey;

        public int SubInventoryDir = -1;

        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }

        public float QuickUseTimer;
        public string QuickUseButtonToolTip;

        public GUIComponent.ComponentState EquipButtonState;
        public Rectangle EquipButtonRect
        {
            get
            {
                int buttonDir = Math.Sign(SubInventoryDir);

                Vector2 equipIndicatorPos = new Vector2(
                    Rect.Center.X - Inventory.EquipIndicator.size.X / 2 * Inventory.UIScale,
                    Rect.Center.Y + (Rect.Height / 2 + 25 * Inventory.UIScale) * buttonDir - Inventory.EquipIndicator.size.Y / 2 * Inventory.UIScale);
                equipIndicatorPos += DrawOffset;

                return new Rectangle(
                    (int)(equipIndicatorPos.X), (int)(equipIndicatorPos.Y),
                    (int)(Inventory.EquipIndicator.size.X * Inventory.UIScale), (int)(Inventory.EquipIndicator.size.Y * Inventory.UIScale));
            }
        }

        public InventorySlot(Rectangle rect)
        {
            Rect = rect;
            InteractRect = rect;
            InteractRect.Inflate(5, 5);
            State = GUIComponent.ComponentState.None;
            Color = Color.White * 0.4f;
        }

        public bool MouseOn()
        {
            Rectangle rect = InteractRect;
            rect.Location += DrawOffset.ToPoint();
            return rect.Contains(PlayerInput.MousePosition);
        }

        public void ShowBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration, float scaleUpAmount = 0.5f)
        {
            if (highlightCoroutine != null)
            {
                CoroutineManager.StopCoroutines(highlightCoroutine);
                highlightCoroutine = null;
            }

            HighlightScaleUpAmount = scaleUpAmount;
            highlightCoroutine = CoroutineManager.StartCoroutine(UpdateBorderHighlight(color, fadeInDuration, fadeOutDuration));
        }

        private IEnumerable<object> UpdateBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            float t = 0.0f;
            HighlightTimer = 1.0f;
            while (t < fadeInDuration + fadeOutDuration)
            {
                HighlightColor = (t < fadeInDuration) ?
                    Color.Lerp(Color.Transparent, color, t / fadeInDuration) :
                    Color.Lerp(color, Color.Transparent, (t - fadeInDuration) / fadeOutDuration);

                t += CoroutineManager.DeltaTime;
                HighlightTimer = 1.0f - t / (fadeInDuration + fadeOutDuration);

                yield return CoroutineStatus.Running;
            }

            HighlightTimer = 0.0f;
            HighlightColor = Color.Transparent;

            yield return CoroutineStatus.Success;
        }
    }

    partial class Inventory
    {
        public static float UIScale
        {
            get { return (GameMain.GraphicsWidth / 1920.0f + GameMain.GraphicsHeight / 1080.0f) / 2.5f * GameSettings.InventoryScale; }
        }

        public static int ContainedIndicatorHeight
        {
            get { return (int)(15 * UIScale); }
        }

        protected float prevUIScale = UIScale;
        protected float prevHUDScale = GUI.Scale;
        protected Point prevScreenResolution;

        protected static Sprite slotHotkeySprite;
        public static Sprite SlotSpriteSmall;
        public static Sprite EquipIndicator, EquipIndicatorHighlight;
        public static Inventory DraggingInventory;

        public Rectangle BackgroundFrame { get; protected set; }

        private ushort[] receivedItemIDs;
        private CoroutineHandle syncItemsCoroutine;

        public float HideTimer;

        private bool isSubInventory;

        private const float movableFrameRectHeight = 40f;
        private Color movableFrameRectColor = new Color(60, 60, 60);
        private Rectangle movableFrameRect;
        private Point savedPosition, originalPos;
        private bool canMove = false;
        private bool positionUpdateQueued = false;

        public class SlotReference
        {
            public readonly Inventory ParentInventory;
            public readonly int SlotIndex;
            public InventorySlot Slot;

            public Inventory Inventory;
            public Item Item;
            public bool IsSubSlot;
            public string Tooltip;
            public List<ColorData> TooltipColorData;

            public SlotReference(Inventory parentInventory, InventorySlot slot, int slotIndex, bool isSubSlot, Inventory subInventory = null)
            {
                ParentInventory = parentInventory;
                Slot = slot;
                SlotIndex = slotIndex;
                Inventory = subInventory;
                IsSubSlot = isSubSlot;
                Item = ParentInventory.Items[slotIndex];
                TooltipColorData = ColorData.GetColorData(GetTooltip(Item), out Tooltip);
            }

            private string GetTooltip(Item item)
            {
                if (item == null) { return null; }

                string toolTip = "";
                if (GameMain.DebugDraw)
                {
                    toolTip = item.ToString();
                }
                else
                {
                    string description = item.Description;
                    if (item.Prefab.Identifier == "idcard" || item.Tags.Contains("despawncontainer"))
                    {
                        string[] readTags = item.Tags.Split(',');
                        string idName = null;
                        string idJob = null;
                        foreach (string tag in readTags)
                        {
                            string[] s = tag.Split(':');
                            if (s[0] == "name")
                                idName = s[1];
                            if (s[0] == "job")
                                idJob = s[1];
                        }
                        if (idName != null)
                        {
                            if (idJob == null)
                            {
                                description = TextManager.GetWithVariable("IDCardName", "[name]", idName);
                            }
                            else
                            {
                                description = TextManager.GetWithVariables("IDCardNameJob", new string[2] { "[name]", "[job]" }, new string[2] { idName, idJob }, new bool[2] { false, true });
                            }
                            if (!string.IsNullOrEmpty(item.Description))
                            {
                                description = description + " " + item.Description;
                            }
                        }
                    }
                    if (item.Prefab.ShowContentsInTooltip && item.OwnInventory != null)
                    {
                        foreach (string itemName in item.OwnInventory.Items.Where(it => it != null).Select(it => it.Name).Distinct())
                        {
                            int itemCount = item.OwnInventory.Items.Count(it => it != null && it.Name == itemName);
                            description += itemCount == 1 ?
                                "\n    " + itemName :
                                "\n    " + itemName + " x" + itemCount;
                        }
                    }

                    toolTip = string.IsNullOrEmpty(description) ?
                        item.Name :
                        item.Name + '\n' + description;
                }

                return toolTip;
            }
        }

        public static InventorySlot draggingSlot;
        public static Item draggingItem;
        public static bool DraggingItemToWorld
        {
            get
            {
                return draggingItem != null &&
                  Character.Controlled != null &&
                  Character.Controlled.SelectedConstruction == null &&
                  CharacterHealth.OpenHealthWindow == null;
            }
        }

        public static Item doubleClickedItem;

        protected Vector4 padding;

        private int slotsPerRow;
        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected static HashSet<SlotReference> highlightedSubInventorySlots = new HashSet<SlotReference>();

        protected static SlotReference selectedSlot;

        public InventorySlot[] slots;

        private Rectangle prevRect;
        /// <summary>
        /// If set, the inventory is automatically positioned inside the rect
        /// </summary>
        public RectTransform RectTransform;

        public static SlotReference SelectedSlot
        {
            get
            {
                if (selectedSlot?.ParentInventory?.Owner == null || selectedSlot.ParentInventory.Owner.Removed)
                {
                    return null;
                }
                return selectedSlot;
            }
        }

        public virtual void CreateSlots()
        {
            slots = new InventorySlot[capacity];

            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);
            int columns = Math.Min(slotsPerRow, capacity);

            Vector2 spacing = new Vector2(5.0f * UIScale);
            spacing.Y += (this is CharacterInventory) ? EquipIndicator.size.Y * UIScale : ContainedIndicatorHeight;
            Vector2 rectSize = new Vector2(60.0f * UIScale);

            padding = new Vector4(spacing.X, spacing.Y, spacing.X, spacing.X);

            Vector2 slotAreaSize = new Vector2(
                columns * rectSize.X + (columns - 1) * spacing.X,
                rows * rectSize.Y + (rows - 1) * spacing.Y);
            slotAreaSize.X += padding.X + padding.Z;
            slotAreaSize.Y += padding.Y + padding.W;

            Vector2 topLeft = new Vector2(
                GameMain.GraphicsWidth / 2 - slotAreaSize.X / 2,
                GameMain.GraphicsHeight / 2 - slotAreaSize.Y / 2);

            if (RectTransform != null)
            {
                Vector2 scale = new Vector2(
                    RectTransform.Rect.Width / slotAreaSize.X,
                    RectTransform.Rect.Height / slotAreaSize.Y);

                spacing *= scale;
                rectSize *= scale;
                padding.X *= scale.X; padding.Z *= scale.X;
                padding.Y *= scale.Y; padding.W *= scale.Y;

                topLeft = RectTransform.TopLeft.ToVector2() + new Vector2(padding.X, padding.Y);
                prevRect = RectTransform.Rect;
            }

            Rectangle slotRect = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)rectSize.X, (int)rectSize.Y);
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(topLeft.X + (rectSize.X + spacing.X) * (i % slotsPerRow));
                slotRect.Y = (int)(topLeft.Y + (rectSize.Y + spacing.Y) * ((int)Math.Floor((double)i / slotsPerRow)));
                slots[i] = new InventorySlot(slotRect);
                slots[i].InteractRect = new Rectangle(
                    (int)(slots[i].Rect.X - spacing.X / 2 - 1), (int)(slots[i].Rect.Y - spacing.Y / 2 - 1),
                    (int)(slots[i].Rect.Width + spacing.X + 2), (int)(slots[i].Rect.Height + spacing.Y + 2));

                if (slots[i].Rect.Width > slots[i].Rect.Height)
                {
                    slots[i].Rect.Inflate((slots[i].Rect.Height - slots[i].Rect.Width) / 2, 0);
                }
                else
                {
                    slots[i].Rect.Inflate(0, (slots[i].Rect.Width - slots[i].Rect.Height) / 2);
                }
            }

            if (selectedSlot != null && selectedSlot.ParentInventory == this)
            {
                selectedSlot = new SlotReference(this, slots[selectedSlot.SlotIndex], selectedSlot.SlotIndex, selectedSlot.IsSubSlot, selectedSlot.Inventory);
            }
            CalculateBackgroundFrame();
        }

        protected virtual void CalculateBackgroundFrame()
        {
        }

        public bool Movable()
        {
            return movableFrameRect.Size != Point.Zero;
        }

        public bool IsInventoryHoverAvailable(Character owner, ItemContainer container)
        {
            if (container == null && this is ItemInventory)
            {
                container = (this as ItemInventory).Container;
            }

            if (container == null) return false;
            return owner.SelectedCharacter != null || !container.KeepOpenWhenEquipped || (!(owner is Character)) || !owner.HasEquippedItem(container.Item);
        }

        protected virtual bool HideSlot(int i)
        {
            return slots[i].Disabled || (hideEmptySlot[i] && Items[i] == null);
        }

        public virtual void Update(float deltaTime, Camera cam, bool subInventory = false)
        {
            if (slots == null || isSubInventory != subInventory ||
                (RectTransform != null && RectTransform.Rect != prevRect))
            {
                CreateSlots();
                isSubInventory = subInventory;
            }

            if (!subInventory || (OpenState >= 0.99f || OpenState < 0.01f))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (HideSlot(i)) { continue; }
                    UpdateSlot(slots[i], i, Items[i], subInventory);
                }
                if (!isSubInventory)
                {
                    ControlInput(cam);
                }
            }
        }

        protected virtual void ControlInput(Camera cam)
        {
            // Note that these targets are static. Therefore the outcome is the same if this method is called multiple times or only once.
            if (selectedSlot != null && !DraggingItemToWorld)
            {
                cam.Freeze = true;
            }
        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            Rectangle interactRect = slot.InteractRect;
            interactRect.Location += slot.DrawOffset.ToPoint();

            bool mouseOnGUI = false;
            /*if (GUI.MouseOn != null)
            {
                //block usage if the mouse is on a GUIComponent that's not related to this inventory
                if (RectTransform == null || (RectTransform != GUI.MouseOn.RectTransform && !GUI.MouseOn.IsParentOf(RectTransform.GUIComponent)))
                {
                    mouseOnGUI = true;
                }
            }*/

            bool mouseOn = interactRect.Contains(PlayerInput.MousePosition) && !Locked && !mouseOnGUI;
            if (PlayerInput.LeftButtonHeld() && PlayerInput.RightButtonHeld())
            {
                mouseOn = false;
            }

            if (selectedSlot != null && selectedSlot.Slot != slot)
            {
                //subinventory slot highlighted -> don't allow highlighting this one
                if (selectedSlot.IsSubSlot && !isSubSlot)
                {
                    mouseOn = false;
                }
                else if (!selectedSlot.IsSubSlot && isSubSlot && mouseOn)
                {
                    selectedSlot = null;
                }
            }


            slot.State = GUIComponent.ComponentState.None;

            if (mouseOn && (draggingItem != null || selectedSlot == null || selectedSlot.Slot == slot) && DraggingInventory == null)
            // &&
            //(highlightedSubInventories.Count == 0 || highlightedSubInventories.Contains(this) || highlightedSubInventorySlot?.Slot == slot || highlightedSubInventory.Owner == item))
            {
                slot.State = GUIComponent.ComponentState.Hover;

                if (selectedSlot == null || (!selectedSlot.IsSubSlot && isSubSlot))
                {
                    selectedSlot = new SlotReference(this, slot, slotIndex, isSubSlot, Items[slotIndex]?.GetComponent<ItemContainer>()?.Inventory);
                }

                if (draggingItem == null)
                {
                    if (PlayerInput.PrimaryMouseButtonDown())
                    {
                        draggingItem = Items[slotIndex];
                        draggingSlot = slot;
                    }
                }
                else if (PlayerInput.PrimaryMouseButtonReleased())
                {
                    if (PlayerInput.DoubleClicked())
                    {
                        doubleClickedItem = item;
                    }
                }
            }
        }

        protected Inventory GetSubInventory(int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return null;

            var container = item.GetComponent<ItemContainer>();
            if (container == null) return null;

            return container.Inventory;
        }

        protected virtual ItemInventory GetActiveEquippedSubInventory(int slotIndex)
        {
            return null;
        }

        public float OpenState;

        public void UpdateSubInventory(float deltaTime, int slotIndex, Camera cam)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) return;

            var subInventory = container.Inventory;
            if (subInventory.slots == null) subInventory.CreateSlots();

            canMove = container.MovableFrame && !subInventory.IsInventoryHoverAvailable(Owner as Character, container) && subInventory.originalPos != Point.Zero;

            if (canMove)
            {
                subInventory.HideTimer = 1.0f;
                subInventory.OpenState = 1.0f;
                if (subInventory.movableFrameRect.Contains(PlayerInput.MousePosition) && PlayerInput.SecondaryMouseButtonClicked())
                {
                    container.Inventory.savedPosition = container.Inventory.originalPos;
                }
                if (subInventory.movableFrameRect.Contains(PlayerInput.MousePosition) || (DraggingInventory != null && DraggingInventory == subInventory))
                {
                    if (DraggingInventory == null)
                    {
                        if (PlayerInput.PrimaryMouseButtonDown())
                        {
                            DraggingInventory = subInventory;
                        }
                    }
                    else if (PlayerInput.PrimaryMouseButtonReleased())
                    {
                        DraggingInventory = null;
                        subInventory.savedPosition = PlayerInput.MousePosition.ToPoint();
                    }
                    else
                    {
                        subInventory.savedPosition = PlayerInput.MousePosition.ToPoint();
                    }
                }
            }

            int itemCapacity = subInventory.Items.Length;
            var slot = slots[slotIndex];
            int dir = slot.SubInventoryDir;
            Rectangle subRect = slot.Rect;
            Vector2 spacing = new Vector2(10 * UIScale, (10 + EquipIndicator.size.Y) * UIScale);

            int columns = (int)Math.Max(Math.Floor(Math.Sqrt(itemCapacity)), 1);
            while (itemCapacity / columns * (subRect.Height + spacing.Y) > GameMain.GraphicsHeight * 0.5f)
            {
                columns++;
            }

            int width = (int)(subRect.Width * columns + spacing.X * (columns - 1));
            int startX = slot.Rect.Center.X - (int)(width / 2.0f);
            int startY = dir < 0 ?
                slot.EquipButtonRect.Y - subRect.Height - (int)(35 * UIScale) :
                slot.EquipButtonRect.Bottom + (int)(10 * UIScale);

            if (canMove)
            {
                startX += subInventory.savedPosition.X - subInventory.originalPos.X;
                startY += subInventory.savedPosition.Y - subInventory.originalPos.Y;
            }

            float totalHeight = itemCapacity / columns * (subRect.Height + spacing.Y);
            int padding = (int)(20 * UIScale);

            //prevent the inventory from extending outside the left side of the screen
            startX = Math.Max(startX, padding);
            //same for the right side of the screen
            startX -= Math.Max(startX + width - GameMain.GraphicsWidth + padding, 0);

            //prevent the inventory from extending outside the top of the screen
            startY = Math.Max(startY, (int)totalHeight - padding / 2);
            //same for the bottom side of the screen
            startY -= Math.Max(startY - GameMain.GraphicsHeight + padding * 2 + (canMove ? (int)(movableFrameRectHeight * UIScale) : 0), 0);               

            subRect.X = startX;
            subRect.Y = startY;

            subInventory.OpenState = subInventory.HideTimer >= 0.5f ?
                Math.Min(subInventory.OpenState + deltaTime * 8.0f, 1.0f) :
                Math.Max(subInventory.OpenState - deltaTime * 5.0f, 0.0f);

            for (int i = 0; i < itemCapacity; i++)
            {
                subInventory.slots[i].Rect = subRect;
                subInventory.slots[i].Rect.Location += new Point(0, (int)totalHeight * -dir);

                subInventory.slots[i].DrawOffset = Vector2.SmoothStep(new Vector2(0, -50 * dir), new Vector2(0, totalHeight * dir), subInventory.OpenState);

                subInventory.slots[i].InteractRect = new Rectangle(
                    (int)(subInventory.slots[i].Rect.X - spacing.X / 2 - 1), (int)(subInventory.slots[i].Rect.Y - spacing.Y / 2 - 1),
                    (int)(subInventory.slots[i].Rect.Width + spacing.X + 2), (int)(subInventory.slots[i].Rect.Height + spacing.Y + 2));

                if ((i + 1) % columns == 0)
                {
                    subRect.X = startX;
                    subRect.Y += subRect.Height * dir;
                    subRect.Y += (int)(spacing.Y * dir);
                }
                else
                {
                    subRect.X = (int)(subInventory.slots[i].Rect.Right + spacing.X);
                }
            }

            if (canMove)
            {
                subInventory.movableFrameRect.X = subRect.X - (int)spacing.X;
                subInventory.movableFrameRect.Y = subRect.Y + (int)(spacing.Y);
            }
            slots[slotIndex].State = GUIComponent.ComponentState.Hover;
            
            subInventory.isSubInventory = true;
            subInventory.Update(deltaTime, cam, true);
        }

        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots == null || isSubInventory != subInventory) return;

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;

                Rectangle interactRect = slots[i].InteractRect;
                interactRect.Location += slots[i].DrawOffset.ToPoint();

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || interactRect.Contains(PlayerInput.MousePosition);

                DrawSlot(spriteBatch, this, slots[i], Items[i], i, drawItem);
            }
        }

        /// <summary>
        /// Is the mouse on any inventory element (slot, equip button, subinventory...)
        /// </summary>
        /// <returns></returns>
        public static bool IsMouseOnInventory()
        {
            if (Character.Controlled == null) return false;

            if (draggingItem != null || DraggingInventory != null) return true;

            if (Character.Controlled.Inventory != null)
            {
                var inv = Character.Controlled.Inventory;
                for (var i = 0; i < inv.slots.Length; i++)
                {
                    var slot = inv.slots[i];
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition))
                    {
                        return true;
                    }

                    // check if the equip button actually exists
                    if (slot.EquipButtonRect.Contains(PlayerInput.MousePosition) && 
                        i >= 0 && inv.Items.Length > i &&
                        inv.Items[i] != null)
                    {
                        return true;
                    }
                }
            }
            if (Character.Controlled.SelectedCharacter?.Inventory != null)
            {
                var inv = Character.Controlled.SelectedCharacter.Inventory;
                for (var i = 0; i < inv.slots.Length; i++)
                {
                    var slot = inv.slots[i];
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition))
                    {
                        return true;
                    }
                    
                    // check if the equip button actually exists
                    if (slot.EquipButtonRect.Contains(PlayerInput.MousePosition) && 
                        i >= 0 && inv.Items.Length > i &&
                        inv.Items[i] != null)
                    {
                        return true;
                    }
                }
            }

            if (Character.Controlled.SelectedConstruction != null)
            {
                foreach (var ic in Character.Controlled.SelectedConstruction.ActiveHUDs)
                {
                    var itemContainer = ic as ItemContainer;
                    if (itemContainer?.Inventory?.slots == null) continue;

                    foreach (InventorySlot slot in itemContainer.Inventory.slots)
                    {
                        if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                            slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (SlotReference highlightedSubInventorySlot in highlightedSubInventorySlots)
            {
                if (GetSubInventoryHoverArea(highlightedSubInventorySlot).Contains(PlayerInput.MousePosition)) return true;
            }

            return false;
        }
        
        public static CursorState GetInventoryMouseCursor()
        {
            var character = Character.Controlled;
            if (character == null) { return CursorState.Default; }
            if (draggingItem != null || DraggingInventory != null) { return CursorState.Dragging; }
            
            var inv = character.Inventory;
            var selInv = character.SelectedCharacter?.Inventory;
            
            if (inv == null) { return CursorState.Default; }

            foreach (var item in inv.Items)
            {
                var container = item?.GetComponent<ItemContainer>();
                if (container == null) { continue; }

                if (container.Inventory.slots != null)
                {
                    if (container.Inventory.slots.Any(slot => slot.IsHighlighted))
                    {
                        return CursorState.Hand;
                    }
                }

                if (container.Inventory.movableFrameRect.Contains(PlayerInput.MousePosition))
                {
                    return CursorState.Move;
                }
            }
            
            
            if (selInv != null)
            {
                foreach (var slot in selInv.slots)
                {
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                        slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                    {
                        return CursorState.Hand;
                    }
                }
            }
            
            if (character.SelectedConstruction != null)
            {
                foreach (var ic in character.SelectedConstruction.ActiveHUDs)
                {
                    var itemContainer = ic as ItemContainer;
                    if (itemContainer?.Inventory?.slots == null) continue;

                    foreach (var slot in itemContainer.Inventory.slots)
                    {
                        if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                            slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                        {
                            return CursorState.Hand;
                        }
                    }
                }
            }

            foreach (var slot in inv.slots)
            {
                if (slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                {
                    return CursorState.Hand;
                }
                
                // This is the only place we double check this because if we have a inventory container
                // highlighting any area within that container registers as highlighting the
                // original slot the item is in thus giving us a false hand cursor.
                if (slot.InteractRect.Contains(PlayerInput.MousePosition))
                {
                    if (slot.IsHighlighted)
                    {
                        return CursorState.Hand;
                    }
                }
            }
            return CursorState.Default;
        }

        protected static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle highlightedSlot, List<ColorData> colorData = null)
        {           
            GUIComponent.DrawToolTip(spriteBatch, toolTip, highlightedSlot, colorData);
        }

        public void DrawSubInventory(SpriteBatch spriteBatch, int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) return;

            if (container.Inventory.slots == null || !container.Inventory.isSubInventory) return;

            int itemCapacity = container.Capacity;

#if DEBUG
            System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < Items.Length);
#else
            if (slotIndex < 0 || slotIndex >= Items.Length) return;
#endif

            if (!canMove)
            {
                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
                if (slots[slotIndex].SubInventoryDir > 0)
                {
                    spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                        new Point(0, slots[slotIndex].Rect.Bottom),
                        new Point(GameMain.GraphicsWidth, (int)Math.Max(GameMain.GraphicsHeight - slots[slotIndex].Rect.Bottom, 0)));
                }
                else
                {
                    spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                        new Point(0, 0),
                        new Point(GameMain.GraphicsWidth, slots[slotIndex].Rect.Y));
                }
                container.Inventory.Draw(spriteBatch, true);
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred);
            }
            else
            {
                container.Inventory.Draw(spriteBatch, true);
            }

            container.InventoryBottomSprite?.Draw(spriteBatch,
                new Vector2(slots[slotIndex].Rect.Center.X, slots[slotIndex].Rect.Y) + slots[slotIndex].DrawOffset,
                0.0f, UIScale);

            container.InventoryTopSprite?.Draw(spriteBatch,
                new Vector2(
                    slots[slotIndex].Rect.Center.X,
                    container.Inventory.slots[container.Inventory.slots.Length - 1].Rect.Y) + container.Inventory.slots[container.Inventory.slots.Length - 1].DrawOffset,
                0.0f, UIScale);

            if (container.MovableFrame && !IsInventoryHoverAvailable(Owner as Character, container))
            {
                if (positionUpdateQueued) // Wait a frame before updating the positioning of the container after a resolution change to have everything working
                {
                    container.Inventory.originalPos = container.Inventory.savedPosition = container.Inventory.movableFrameRect.Center;
                    positionUpdateQueued = false;
                }

                if (container.Inventory.movableFrameRect.Size == Point.Zero || GUI.HasSizeChanged(prevScreenResolution, prevUIScale, prevHUDScale))
                {
                    // Reset position
                    container.Inventory.savedPosition = container.Inventory.originalPos;

                    prevScreenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                    prevUIScale = UIScale;
                    prevHUDScale = GUI.Scale;
                    int height = (int)(movableFrameRectHeight * UIScale);
                    container.Inventory.movableFrameRect = new Rectangle(container.Inventory.BackgroundFrame.X, container.Inventory.BackgroundFrame.Y - height, container.Inventory.BackgroundFrame.Width, height);
                    positionUpdateQueued = true;
                }
                
                GUI.DrawRectangle(spriteBatch, container.Inventory.movableFrameRect, movableFrameRectColor, true);
            }
        }

        public static void UpdateDragging()
        {
            if (draggingItem != null && PlayerInput.PrimaryMouseButtonReleased())
            {
                Character.Controlled.ClearInputs();

                if (CharacterHealth.OpenHealthWindow != null &&
                    CharacterHealth.OpenHealthWindow.OnItemDropped(draggingItem, false))
                {
                    draggingItem = null;
                    return;
                }

                if (selectedSlot == null)
                {
                    if (DraggingItemToWorld &&
                        Character.Controlled.FocusedItem?.OwnInventory != null &&
                        Character.Controlled.FocusedItem.OwnInventory.CanBePut(draggingItem) &&
                        Character.Controlled.FocusedItem.OwnInventory.TryPutItem(draggingItem, Character.Controlled))
                    {
                        GUI.PlayUISound(GUISoundType.PickItem);
                    }
                    else
                    {
                        GUI.PlayUISound(GUISoundType.DropItem);
                        draggingItem.Drop(Character.Controlled);
                    }
                }
                else if (selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex] != draggingItem)
                {
                    Inventory selectedInventory = selectedSlot.ParentInventory;
                    int slotIndex = selectedSlot.SlotIndex;

                    //if attempting to drop into an invalid slot in the same inventory, try to move to the correct slot
                    if (selectedInventory.Items[slotIndex] == null &&
                        selectedInventory == Character.Controlled.Inventory &&
                        !draggingItem.AllowedSlots.Any(a => a.HasFlag(Character.Controlled.Inventory.SlotTypes[slotIndex])) &&
                        selectedInventory.TryPutItem(draggingItem, Character.Controlled, draggingItem.AllowedSlots))
                    {
                        if (selectedInventory.slots != null)
                        {
                            for (int i = 0; i < selectedInventory.slots.Length; i++)
                            {
                                if (selectedInventory.Items[i] == draggingItem)
                                {
                                    selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                                }
                            }
                            selectedInventory.slots[slotIndex].ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.9f);
                        }
                        GUI.PlayUISound(GUISoundType.PickItem);
                    }
                    else if (selectedInventory.TryPutItem(draggingItem, slotIndex, true, true, Character.Controlled))
                    {
                        if (selectedInventory.slots != null) { selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f); }
                        GUI.PlayUISound(GUISoundType.PickItem);
                    }
                    else
                    {
                        if (selectedInventory.slots != null){ selectedInventory.slots[slotIndex].ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.9f); }
                        GUI.PlayUISound(GUISoundType.PickItemFail);
                    }
                    selectedInventory.HideTimer = 2.0f;
                    if (selectedSlot.ParentInventory?.Owner is Item parentItem && parentItem.ParentInventory != null)
                    {
                        for (int i = 0; i < parentItem.ParentInventory.capacity; i++)
                        {
                            if (parentItem.ParentInventory.HideSlot(i)) continue;
                            if (parentItem.ParentInventory.Items[i] != parentItem) continue;

                            highlightedSubInventorySlots.Add(new SlotReference(
                                parentItem.ParentInventory, parentItem.ParentInventory.slots[i],
                                i, false, selectedSlot.ParentInventory));
                            break;
                        }

                    }
                    draggingItem = null;
                    draggingSlot = null;
                }

                draggingItem = null;
            }

            if (selectedSlot != null && !selectedSlot.Slot.MouseOn())
            {
                selectedSlot = null;
            }
        }

        protected static Rectangle GetSubInventoryHoverArea(SlotReference subSlot)
        {
            Rectangle hoverArea;
            if (!subSlot.Inventory.Movable())
            {
                hoverArea = subSlot.Slot.Rect;
                hoverArea.Location += subSlot.Slot.DrawOffset.ToPoint();
                hoverArea = Rectangle.Union(hoverArea, subSlot.Slot.EquipButtonRect);
            }
            else
            {
                hoverArea = subSlot.Inventory.BackgroundFrame;
                hoverArea.Location += subSlot.Slot.DrawOffset.ToPoint();
                hoverArea = Rectangle.Union(hoverArea, subSlot.Inventory.movableFrameRect);
            }

            if (subSlot.Inventory?.slots != null)
            {
                foreach (InventorySlot slot in subSlot.Inventory.slots)
                {
                    Rectangle subSlotRect = slot.InteractRect;
                    subSlotRect.Location += slot.DrawOffset.ToPoint();
                    hoverArea = Rectangle.Union(hoverArea, subSlotRect);
                }
                if (subSlot.Slot.SubInventoryDir < 0)
                {
                    hoverArea.Height -= hoverArea.Bottom - subSlot.Slot.Rect.Bottom;
                }
                else
                {
                    int over = subSlot.Slot.Rect.Y - hoverArea.Y;
                    hoverArea.Y += over;
                    hoverArea.Height -= over;
                }
            }

            float inflateAmount = 10 * UIScale;

            hoverArea.Inflate(inflateAmount, inflateAmount);
            return hoverArea;
        }

        public static void DrawFront(SpriteBatch spriteBatch)
        {
            if (GUI.PauseMenuOpen || GUI.SettingsMenuOpen) return;

            foreach (var slot in highlightedSubInventorySlots)
            {
                int slotIndex = Array.IndexOf(slot.ParentInventory.slots, slot.Slot);
                if (slotIndex > -1 && slotIndex < slot.ParentInventory.slots.Length)
                {
                    slot.ParentInventory.DrawSubInventory(spriteBatch, slotIndex);
                }
            }  

            if (draggingItem != null)
            {
                if (draggingSlot == null || (!draggingSlot.MouseOn()))
                {
                    Sprite sprite = draggingItem.Prefab.InventoryIcon ?? draggingItem.Sprite;

                    int iconSize = (int)(64 * GUI.Scale);
                    float scale = Math.Min(Math.Min(iconSize / sprite.size.X, iconSize / sprite.size.Y), 1.5f);
                    Vector2 itemPos = PlayerInput.MousePosition;

                    bool mouseOnHealthInterface = CharacterHealth.OpenHealthWindow != null && CharacterHealth.OpenHealthWindow.MouseOnElement;

                    if ((GUI.MouseOn == null || mouseOnHealthInterface) && selectedSlot == null)
                    {
                        var shadowSprite = GUI.Style.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0];
                        string toolTip = mouseOnHealthInterface ? TextManager.Get("QuickUseAction.UseTreatment") :
                            Character.Controlled.FocusedItem != null ?
                                TextManager.GetWithVariable("PutItemIn", "[itemname]", Character.Controlled.FocusedItem.Name, true) :
                                TextManager.Get("DropItem");
                        int textWidth = (int)Math.Max(GUI.Font.MeasureString(draggingItem.Name).X, GUI.SmallFont.MeasureString(toolTip).X);
                        int textSpacing = (int)(15 * GUI.Scale);
                        Point shadowBorders = (new Point(40, 10)).Multiply(GUI.Scale);
                        shadowSprite.Draw(spriteBatch,
                            new Rectangle(itemPos.ToPoint() - new Point(iconSize / 2) - shadowBorders, new Point(iconSize + textWidth + textSpacing, iconSize) + shadowBorders.Multiply(2)), Color.Black * 0.8f);
                        GUI.DrawString(spriteBatch, new Vector2(itemPos.X + iconSize / 2 + textSpacing, itemPos.Y - iconSize / 2), draggingItem.Name, Color.White);
                        GUI.DrawString(spriteBatch, new Vector2(itemPos.X + iconSize / 2 + textSpacing, itemPos.Y), toolTip,
                            color: Character.Controlled.FocusedItem == null && !mouseOnHealthInterface ? GUI.Style.Red : Color.LightGreen,
                            font: GUI.SmallFont);
                    }
                    sprite.Draw(spriteBatch, itemPos + Vector2.One * 2, Color.Black, scale: scale);
                    sprite.Draw(spriteBatch,
                        itemPos,
                        sprite == draggingItem.Sprite ? draggingItem.GetSpriteColor() : draggingItem.GetInventoryIconColor(),
                        scale: scale);
                }
            }

            if (selectedSlot != null && selectedSlot.Item != null)
            {
                Rectangle slotRect = selectedSlot.Slot.Rect;
                slotRect.Location += selectedSlot.Slot.DrawOffset.ToPoint();
                DrawToolTip(spriteBatch, selectedSlot.Tooltip, slotRect, selectedSlot.TooltipColorData);
            }
        }

        public static void DrawSlot(SpriteBatch spriteBatch, Inventory inventory, InventorySlot slot, Item item, int slotIndex, bool drawItem = true, InvSlotType type = InvSlotType.Any)
        {
            Rectangle rect = slot.Rect;
            rect.Location += slot.DrawOffset.ToPoint();

            if (slot.HighlightColor.A > 0)
            {
                float inflateAmount = (slot.HighlightColor.A / 255.0f) * slot.HighlightScaleUpAmount * 0.5f;
                rect.Inflate(rect.Width * inflateAmount, rect.Height * inflateAmount);
            }

            Color slotColor = Color.White;
            var itemContainer = item?.GetComponent<ItemContainer>();
            if (itemContainer != null && (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null))
            {
                if (!highlightedSubInventorySlots.Any(s => s.Slot == slot))
                {
                    itemContainer.InventoryBottomSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                    itemContainer.InventoryTopSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                }

                drawItem = false;
            }
            else
            {
                Sprite slotSprite = slot.SlotSprite ?? SlotSpriteSmall;

                if (inventory != null && (CharacterInventory.PersonalSlots.HasFlag(type) || (inventory.isSubInventory && (inventory.Owner as Item) != null 
                    && (inventory.Owner as Item).AllowedSlots.Any(a => CharacterInventory.PersonalSlots.HasFlag(a)))))
                {
                    slotColor = slot.IsHighlighted ? GUIColorSettings.EquipmentSlotColor : GUIColorSettings.EquipmentSlotColor * 0.8f;
                }
                else
                {
                    slotColor = slot.IsHighlighted ? GUIColorSettings.InventorySlotColor : GUIColorSettings.InventorySlotColor * 0.8f;
                }

                if (inventory != null && inventory.Locked) { slotColor = Color.Gray * 0.5f; }
                spriteBatch.Draw(slotSprite.Texture, rect, slotSprite.SourceRect, slotColor);

                bool canBePut = false;

                if (draggingItem != null && inventory != null && slotIndex > -1 && slotIndex < inventory.slots.Length)
                {
                    if (inventory.CanBePut(draggingItem, slotIndex))
                    {
                        canBePut = true;
                    }
                    else if (inventory.Items[slotIndex]?.OwnInventory?.CanBePut(draggingItem) ?? false)
                    {
                        canBePut = true;
                    }
                    else if (inventory.Items[slotIndex] == null && inventory == Character.Controlled.Inventory && 
                        !draggingItem.AllowedSlots.Any(a => a.HasFlag(Character.Controlled.Inventory.SlotTypes[slotIndex])) &&
                        Character.Controlled.Inventory.CanBeAutoMovedToCorrectSlots(draggingItem))
                    {
                        canBePut = true;
                    }
                }
                if (slot.MouseOn() && canBePut)
                {
                    GUI.UIGlow.Draw(spriteBatch, rect, GUI.Style.Green);
                }

                if (item != null && drawItem)
                {
                    if (!item.IsFullCondition && (itemContainer == null || !itemContainer.ShowConditionInContainedStateIndicator))
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black * 0.8f, true);
                        GUI.DrawRectangle(spriteBatch,
                            new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * (item.Condition / item.MaxCondition)), 8),
                            Color.Lerp(GUI.Style.Red, GUI.Style.Green, item.Condition / item.MaxCondition) * 0.8f, true);
                    }

                    if (itemContainer != null && itemContainer.ShowContainedStateIndicator)
                    {
                        float containedState = 0.0f;
                        if (itemContainer.ShowConditionInContainedStateIndicator)
                        {
                            containedState = item.Condition / item.MaxCondition;
                        }
                        else
                        {
                            containedState = itemContainer.Inventory.Capacity == 1 ?
                                (itemContainer.Inventory.Items[0] == null ? 0.0f : itemContainer.Inventory.Items[0].Condition / itemContainer.Inventory.Items[0].MaxCondition) :
                                itemContainer.Inventory.Items.Count(i => i != null) / (float)itemContainer.Inventory.capacity;
                        }

                        int dir = slot.SubInventoryDir;
                        Rectangle containedIndicatorArea = new Rectangle(rect.X,
                            dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - ContainedIndicatorHeight, rect.Width, ContainedIndicatorHeight);
                        containedIndicatorArea.Inflate(-4, 0);

                        if (itemContainer.ContainedStateIndicator?.Texture == null)
                        {
                            containedIndicatorArea.Inflate(0, -2);
                            GUI.DrawRectangle(spriteBatch, containedIndicatorArea, Color.Gray * 0.9f, true);
                            GUI.DrawRectangle(spriteBatch,
                                new Rectangle(containedIndicatorArea.X, containedIndicatorArea.Y, (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Height),
                                ToolBox.GradientLerp(containedState, Color.Red, Color.Orange, Color.LightGreen) * 0.8f, true);
                            GUI.DrawLine(spriteBatch, 
                                new Vector2(containedIndicatorArea.X + (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Y),
                                new Vector2(containedIndicatorArea.X + (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Bottom),
                                Color.Black * 0.8f);
                        }
                        else
                        {
                            Sprite indicatorSprite = itemContainer.ContainedStateIndicator;
                            float indicatorScale = Math.Min(
                                containedIndicatorArea.Width / (float)indicatorSprite.SourceRect.Width,
                                containedIndicatorArea.Height / (float)indicatorSprite.SourceRect.Height);

                            if (containedState >= 0.0f && containedState < 0.25f && inventory == Character.Controlled?.Inventory && Character.Controlled.HasEquippedItem(item))
                            {
                                indicatorScale += ((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) * 0.25f;
                            }

                            indicatorSprite.Draw(spriteBatch, containedIndicatorArea.Center.ToVector2(),
                                (inventory != null && inventory.Locked) ? Color.Gray * 0.5f : Color.Gray * 0.9f,
                                origin: indicatorSprite.size / 2,
                                rotate: 0.0f,
                                scale: indicatorScale);

                            Color indicatorColor = ToolBox.GradientLerp(containedState, Color.Red, Color.Orange, Color.LightGreen);
                            if (inventory != null && inventory.Locked) { indicatorColor *= 0.5f; }

                            spriteBatch.Draw(indicatorSprite.Texture, containedIndicatorArea.Center.ToVector2(),
                                sourceRectangle: new Rectangle(indicatorSprite.SourceRect.Location, new Point((int)(indicatorSprite.SourceRect.Width * containedState), indicatorSprite.SourceRect.Height)),
                                color: indicatorColor,
                                rotation: 0.0f,
                                origin: indicatorSprite.size / 2,
                                scale: indicatorScale,
                                effects: SpriteEffects.None, layerDepth: 0.0f);

                            spriteBatch.Draw(indicatorSprite.Texture, containedIndicatorArea.Center.ToVector2(),
                                sourceRectangle: new Rectangle(indicatorSprite.SourceRect.X - 1 + (int)(indicatorSprite.SourceRect.Width * containedState), indicatorSprite.SourceRect.Y, Math.Max((int)Math.Ceiling(1 / indicatorScale), 2), indicatorSprite.SourceRect.Height),
                                color: Color.Black,
                                rotation: 0.0f,
                                origin: new Vector2(indicatorSprite.size.X * (0.5f - containedState), indicatorSprite.size.Y * 0.5f),
                                scale: indicatorScale,
                                effects: SpriteEffects.None, layerDepth: 0.0f);
                        }
                    }
                }
            }

            if (GameMain.DebugDraw)
            {
                GUI.DrawRectangle(spriteBatch, rect, Color.White, false, 0, 1);
                GUI.DrawRectangle(spriteBatch, slot.EquipButtonRect, Color.White, false, 0, 1);
            }

            if (slot.HighlightColor != Color.Transparent)
            {
                GUI.UIGlow.Draw(spriteBatch, rect, slot.HighlightColor);
            }

            if (item != null && drawItem)
            {
                Sprite sprite = item.Prefab.InventoryIcon ?? item.Sprite;
                float scale = Math.Min(Math.Min((rect.Width - 10) / sprite.size.X, (rect.Height - 10) / sprite.size.Y), 2.0f);
                Vector2 itemPos = rect.Center.ToVector2();
                if (itemPos.Y > GameMain.GraphicsHeight)
                {
                    itemPos.Y -= Math.Min(
                        (itemPos.Y + sprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
                        (itemPos.Y - sprite.size.Y / 2 * scale) - rect.Y);
                }

                float rotation = 0.0f;
                if (slot.HighlightColor.A > 0)
                {
                    rotation = (float)Math.Sin(slot.HighlightTimer * MathHelper.TwoPi) * slot.HighlightTimer * 0.3f;
                }

                Color spriteColor = sprite == item.Sprite ? item.GetSpriteColor() : item.GetInventoryIconColor();
                if (inventory != null && inventory.Locked) { spriteColor *= 0.5f; }
                if (CharacterHealth.OpenHealthWindow != null && !item.UseInHealthInterface)
                {
                    spriteColor = Color.Lerp(spriteColor, Color.TransparentBlack, 0.5f);
                }
                else
                {
                    sprite.Draw(spriteBatch, itemPos + Vector2.One * 2, Color.Black * 0.6f, rotate: rotation, scale: scale);
                }
                sprite.Draw(spriteBatch, itemPos, spriteColor, rotation, scale);
            }

            if (inventory != null &&
                !inventory.Locked &&
                Character.Controlled?.Inventory == inventory &&
                slot.QuickUseKey != Keys.None)
            {
                spriteBatch.Draw(slotHotkeySprite.Texture, rect.ScaleSize(1.25f), slotHotkeySprite.SourceRect, slotColor);
                GUI.DrawString(spriteBatch, rect.Location.ToVector2() + new Vector2(1, -2), slot.QuickUseKey.ToString().Substring(1, 1), Color.Black, font: GUI.SmallFont);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            UInt16 lastEventID = msg.ReadUInt16();
            byte itemCount = msg.ReadByte();
            receivedItemIDs = new ushort[itemCount];
            for (int i = 0; i < itemCount; i++)
            {
                receivedItemIDs[i] = msg.ReadUInt16();
            }

            //delay applying the new state if less than 1 second has passed since this client last sent a state to the server
            //prevents the inventory from briefly reverting to an old state if items are moved around in quick succession

            //also delay if we're still midround syncing, some of the items in the inventory may not exist yet
            if (syncItemsDelay > 0.0f || GameMain.Client.MidRoundSyncing)
            {
                if (syncItemsCoroutine != null) CoroutineManager.StopCoroutines(syncItemsCoroutine);
                syncItemsCoroutine = CoroutineManager.StartCoroutine(SyncItemsAfterDelay(lastEventID));
            }
            else
            {
                if (syncItemsCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(syncItemsCoroutine);
                    syncItemsCoroutine = null;
                }
                ApplyReceivedState();
            }
        }

        private IEnumerable<object> SyncItemsAfterDelay(UInt16 lastEventID)
        {
            while (syncItemsDelay > 0.0f || 
                //don't apply inventory updates until 
                //  1. MidRound syncing is done AND
                //  2. We've received all the events created before the update was written (otherwise we may not yet know about some items the server has spawned in the inventory)
                (GameMain.Client != null && (GameMain.Client.MidRoundSyncing || NetIdUtils.IdMoreRecent(lastEventID, GameMain.Client.EntityEventManager.LastReceivedID))))
            {
                if (GameMain.GameSession == null || Level.Loaded == null) 
                {
                    yield return CoroutineStatus.Success;
                }
                syncItemsDelay = Math.Max((float)(syncItemsDelay - Timing.Step), 0.0f);
                yield return CoroutineStatus.Running;
            }

            if (Owner.Removed || GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            ApplyReceivedState();

            yield return CoroutineStatus.Success;
        }

        private void ApplyReceivedState()
        {
            if (receivedItemIDs == null || (Owner != null && Owner.Removed)) { return; }

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] == 0 || (Entity.FindEntityByID(receivedItemIDs[i]) as Item != Items[i]))
                {
                    if (Items[i] != null) Items[i].Drop(null);
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] > 0)
                {
                    if (!(Entity.FindEntityByID(receivedItemIDs[i]) is Item item) || Items[i] == item) { continue; }

                    TryPutItem(item, i, true, true, null, false);
                    for (int j = 0; j < capacity; j++)
                    {
                        if (Items[j] == item && receivedItemIDs[j] != item.ID)
                        {
                            Items[j] = null;
                        }
                    }
                }
            }

            receivedItemIDs = null;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            SharedWrite(msg, extraData);
            syncItemsDelay = 1.0f;
        }
    }
}
