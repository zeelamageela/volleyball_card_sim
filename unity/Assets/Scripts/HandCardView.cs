using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VolleyballCore;

/// <summary>
/// Visual representation of one card in the hand-strip UI ("Card Canvas/Card Parent").
/// Attach to the card prefab. GameRunner instantiates one per hand card, calls SetCard()
/// with the real Card data, and sets DropCamera/OnDroppedOnTransform right after --
/// see GameRunner.PopulateHandStrip.
///
/// Drag behavior: follows the pointer (this is a Screen Space Overlay canvas, so screen
/// position IS canvas position, no conversion needed), reparents to the canvas root
/// while dragging so it draws above every other UI element, and on release does a 3D
/// physics raycast (not a UI raycast -- the drop targets are the player capsules in the
/// world, not other UI) from DropCamera through the release point. Always snaps back
/// to its original slot afterward; GameRunner decides what a hit means (or doesn't) and
/// tears down/repopulates the whole strip once a decision actually resolves.
/// </summary>
public class HandCardView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private Color redCardColor = new(0.85f, 0.2f, 0.2f, 0.9f);
    [SerializeField] private Color blackCardColor = new(0.15f, 0.15f, 0.15f, 0.9f);

    /// <summary>Camera to raycast the drop point with -- set by GameRunner (whichever
    /// camera is actually active while a decision is pending).</summary>
    public Camera DropCamera { get; set; }

    /// <summary>Called on drop with the world Transform hit (null if the drop didn't
    /// land on anything with a collider). GameRunner supplies this.</summary>
    public Action<HandCardView, Transform> OnDroppedOnTransform { get; set; }

    public Card CardValue { get; private set; }

    private RectTransform _rectTransform;
    private Transform _originalParent;
    private int _originalSiblingIndex;
    private Vector2 _originalAnchoredPosition;

    private void Awake()
    {
        _rectTransform = (RectTransform)transform;
        if (background == null)
        {
            background = GetComponent<Image>();
        }
        if (valueText == null)
        {
            valueText = GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    public void SetCard(Card card)
    {
        CardValue = card;
        if (valueText != null)
        {
            valueText.text = card.Value.ToString();
        }
        if (background != null)
        {
            background.color = card.Color == CardColor.Red ? redCardColor : blackCardColor;
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _originalParent = transform.parent;
        _originalSiblingIndex = transform.GetSiblingIndex();
        _originalAnchoredPosition = _rectTransform.anchoredPosition;

        // Reparent to the top-level canvas so this card draws above the rest of the
        // hand strip (and anything else in the canvas) while it's being dragged.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            transform.SetParent(canvas.transform, worldPositionStays: true);
            transform.SetAsLastSibling();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Screen Space Overlay canvas: screen position doubles as canvas position.
        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Transform hit = null;
        if (DropCamera != null)
        {
            // eventData.position is in Screen.width/height terms (from the EventSystem);
            // Camera.ScreenPointToRay expects camera.pixelWidth/pixelHeight terms. These
            // are normally identical, but can genuinely diverge (seen live: 939x1111 vs.
            // 1920x1080) under certain Editor Game View resolution-simulation setups --
            // rescale explicitly rather than assume the two always match.
            Vector2 cameraSpacePos = new Vector2(
                eventData.position.x * (DropCamera.pixelWidth / (float)Screen.width),
                eventData.position.y * (DropCamera.pixelHeight / (float)Screen.height));
            Ray ray = DropCamera.ScreenPointToRay(cameraSpacePos);
            if (Physics.Raycast(ray, out RaycastHit hitInfo))
            {
                hit = hitInfo.transform;
            }
        }

        // Snap back first -- if the drop resolves the active decision, GameRunner tears
        // down and repopulates the whole strip on its next Update(), destroying this
        // object anyway; if it doesn't, the card needs to visibly return to hand.
        transform.SetParent(_originalParent, worldPositionStays: false);
        transform.SetSiblingIndex(_originalSiblingIndex);
        _rectTransform.anchoredPosition = _originalAnchoredPosition;

        OnDroppedOnTransform?.Invoke(this, hit);
    }
}
