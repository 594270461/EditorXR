﻿#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.EditorVR.Core;
using UnityEditor.Experimental.EditorVR.Modules;
using UnityEditor.Experimental.EditorVR.Utilities;
using UnityEngine;
using UnityEngine.InputNew;

namespace UnityEditor.Experimental.EditorVR.Menus
{
	sealed class PinnedToolsMenu : MonoBehaviour, IPinnedToolsMenu, IConnectInterfaces, IInstantiateUI,
		IControlHaptics, IUsesViewerScale, IControlSpatialScrolling, IControlSpatialHinting, IRayVisibilitySettings, IUsesRayOrigin
	{
		const int k_ActiveToolOrderPosition = 1; // A active-tool button position used in this particular ToolButton implementation
		const int k_MaxButtonCount = 16;

		[SerializeField]
		Sprite m_MainMenuIcon;

		[SerializeField]
		ActionMap m_MainMenuActionMap;

		[SerializeField]
		PinnedToolsMenuUI m_PinnedToolsMenuPrefab;

		[SerializeField]
		PinnedToolButton m_PinnedToolButtonTemplate;

		[SerializeField]
		HapticPulse m_ButtonClickPulse;

		[SerializeField]
		HapticPulse m_ButtonHoverPulse;

		[SerializeField]
		HapticPulse m_HidingPulse; // The pulse performed when ending a spatial selection

		Transform m_RayOrigin;
		float m_AllowToolToggleBeforeThisTime;
		Vector3 m_SpatialScrollStartPosition;
		PinnedToolsMenuUI m_PinnedToolsMenuUI;

		public Transform menuOrigin { get; set; }
		List<IPinnedToolButton> buttons { get { return m_PinnedToolsMenuUI.buttons; } }
		public bool alternateMenuVisible { set { m_PinnedToolsMenuUI.moveToAlternatePosition = value; } }

		public Action<Transform, int, bool> highlightSingleButton { get; set; }
		public Action<Transform> selectHighlightedButton { get; set; }
		public Action<Type, Sprite> setButtonForType { get; set; }
		public Action<Type, Type> deletePinnedToolButton { get; set; }
		public Node? node { get; set; }
		public IPinnedToolButton previewToolButton { get; private set; }
		public Transform alternateMenuOrigin { get; set; }
		public SpatialScrollModule.SpatialScrollData spatialScrollData { get; set; }
		public ActionMap actionMap { get { return m_MainMenuActionMap; } }

		public Transform rayOrigin
		{
			get { return m_RayOrigin; }
			set
			{
				m_RayOrigin = value;
				// UI is created after RayOrigin is set here
				// Ray origin is then set in CreatePinnedToolsUI()
				CreatePinnedToolsUI();
			}
		}

		void Awake()
		{
			setButtonForType = CreatePinnedToolButton;
			deletePinnedToolButton = DeletePinnedToolButton;
		}

		void OnDestroy()
		{
			this.RemoveRayVisibilitySettings(rayOrigin, this);
		}

		void CreatePinnedToolsUI()
		{
			m_PinnedToolsMenuUI = m_PinnedToolsMenuUI ?? this.InstantiateUI(m_PinnedToolsMenuPrefab.gameObject).GetComponent<PinnedToolsMenuUI>();
			m_PinnedToolsMenuUI.maxButtonCount = k_MaxButtonCount;
			m_PinnedToolsMenuUI.mainMenuActivatorSelected = this.MainMenuActivatorSelected;
			m_PinnedToolsMenuUI.rayOrigin = rayOrigin;
			m_PinnedToolsMenuUI.buttonHovered += OnButtonHover;
			m_PinnedToolsMenuUI.buttonClicked += OnButtonClick;
			m_PinnedToolsMenuUI.buttonSelected += OnButtonSelected;
			m_PinnedToolsMenuUI.closeMenu += CloseMenu;

			// Alternate menu origin isn't set when awake or start run
			var pinnedToolsUITransform = m_PinnedToolsMenuUI.transform;
			pinnedToolsUITransform.SetParent(alternateMenuOrigin);
			pinnedToolsUITransform.localPosition = Vector3.zero;
			pinnedToolsUITransform.localRotation = Quaternion.identity;
		}

		void CreatePinnedToolButton(Type toolType, Sprite buttonIcon)
		{
			// Select an existing ToolButton if the type is already present in a button
			if (buttons.Any( x => x.toolType == toolType))
			{
				m_PinnedToolsMenuUI.SelectExistingToolType(toolType);
				return;
			}

			if (buttons.Count >= k_MaxButtonCount) // Return if tool type already occupies a pinned tool button
				return;

			var buttonTransform = ObjectUtils.Instantiate(m_PinnedToolButtonTemplate.gameObject, m_PinnedToolsMenuUI.buttonContainer, false).transform;
			var button = buttonTransform.GetComponent<PinnedToolButton>();
			this.ConnectInterfaces(button);

			button.rayOrigin = rayOrigin;
			button.toolType = toolType; // Assign Tool Type before assigning order
			button.icon = toolType != typeof(IMainMenu) ? buttonIcon : m_MainMenuIcon;
			button.highlightSingleButton = highlightSingleButton;
			button.selectHighlightedButton = selectHighlightedButton;
			button.rayOrigin = rayOrigin;

			if (toolType == typeof(IMainMenu))
				previewToolButton = button;

			m_PinnedToolsMenuUI.AddButton(button, buttonTransform);
		}

		void DeletePinnedToolButton(Type toolTypeToDelete, Type toolTypeToSelectAfterDelete)
		{
			if (m_PinnedToolsMenuUI.DeleteButtonOfType(toolTypeToDelete))
				m_PinnedToolsMenuUI.SelectNextExistingToolButton();
		}

		public void ProcessInput(ActionMapInput input, ConsumeControlDelegate consumeControl)
		{
			var buttonCount = buttons.Count;
			if (buttonCount <= k_ActiveToolOrderPosition + 1)
				return;

			const float kAllowToggleDuration = 0.25f;
			var pinnedToolInput = (PinnedToolslMenuInput) input;

			if (spatialScrollData != null && pinnedToolInput.cancel.wasJustPressed)
			{
				consumeControl(pinnedToolInput.cancel);
				consumeControl(pinnedToolInput.show);
				consumeControl(pinnedToolInput.select);
				OnButtonClick();
				CloseMenu(); // Also ends spatial scroll
				m_PinnedToolsMenuUI.allButtonsVisible = false;
			}

			if (spatialScrollData == null && (pinnedToolInput.show.wasJustPressed || pinnedToolInput.show.isHeld) && pinnedToolInput.select.wasJustPressed)
			{
				m_SpatialScrollStartPosition = alternateMenuOrigin.position;
				m_AllowToolToggleBeforeThisTime = Time.realtimeSinceStartup + kAllowToggleDuration;
				this.SetSpatialHintControlObject(rayOrigin);
				m_PinnedToolsMenuUI.spatiallyScrolling = true; // Triggers the display of the directional hint arrows
				consumeControl(pinnedToolInput.show);
				consumeControl(pinnedToolInput.select);
				// Assign initial SpatialScrollData; begin scroll
				spatialScrollData = this.PerformSpatialScroll(this, node, m_SpatialScrollStartPosition, alternateMenuOrigin.position, 0.325f, m_PinnedToolsMenuUI.buttons.Count, m_PinnedToolsMenuUI.maxButtonCount);
			}
			else if (spatialScrollData != null && pinnedToolInput.show.isHeld)
			{
				consumeControl(pinnedToolInput.show);
				consumeControl(pinnedToolInput.select);
				// Attempt to close a button, if a scroll has passed the trigger threshold
				if (spatialScrollData != null && pinnedToolInput.select.wasJustPressed)
				{
					if (m_PinnedToolsMenuUI.DeleteHighlightedButton())
						buttonCount = buttons.Count; // The MainMenu button will be hidden, subtract 1 from the activeButtonCount

					if (buttonCount <= k_ActiveToolOrderPosition + 1)
					{
						if (spatialScrollData != null)
							this.EndSpatialScroll(this);

						return;
					}
				}

				// normalized input should loop after reaching the 0.15f length
				buttonCount -= 1; // Decrement to disallow cycling through the main menu button
				spatialScrollData = this.PerformSpatialScroll(this, node, m_SpatialScrollStartPosition, alternateMenuOrigin.position, 0.325f, m_PinnedToolsMenuUI.buttons.Count, m_PinnedToolsMenuUI.maxButtonCount);
				var normalizedRepeatingPosition = spatialScrollData.normalizedLoopingPosition;
				if (!Mathf.Approximately(normalizedRepeatingPosition, 0f))
				{
					if (!m_PinnedToolsMenuUI.allButtonsVisible)
					{
						m_PinnedToolsMenuUI.spatialDragDistance = spatialScrollData.dragDistance;
						this.SetSpatialHintState(SpatialHintModule.SpatialHintStateFlags.CenteredScrolling);
						m_PinnedToolsMenuUI.allButtonsVisible = true;
					}
					else if (spatialScrollData.spatialDirection != null)
					{
						m_PinnedToolsMenuUI.startingDragOrigin = spatialScrollData.spatialDirection;
					}

					m_PinnedToolsMenuUI.HighlightSingleButtonWithoutMenu((int)(buttonCount * normalizedRepeatingPosition) + 1);
				}
			}
			else if (pinnedToolInput.show.wasJustReleased)
			{
				consumeControl(pinnedToolInput.show);
				consumeControl(pinnedToolInput.select);

				if (spatialScrollData != null && spatialScrollData.passedMinDragActivationThreshold)
				{
					m_PinnedToolsMenuUI.SelectHighlightedButton();
				}
				else if (Time.realtimeSinceStartup < m_AllowToolToggleBeforeThisTime)
				{
					// Allow for single press+release to cycle through tools
					m_PinnedToolsMenuUI.SelectNextExistingToolButton();
					OnButtonClick();
				}

				CloseMenu();
			}
		}

		void OnButtonClick()
		{
			this.Pulse(node, m_ButtonClickPulse);
			this.SetSpatialHintState(SpatialHintModule.SpatialHintStateFlags.Hidden);
		}

		void OnButtonHover()
		{
			this.Pulse(node, m_ButtonHoverPulse);
		}

		void OnButtonSelected(Transform rayOrigin, Type buttonType)
		{
			this.SelectTool(rayOrigin, buttonType, false);
		}

		void CloseMenu()
		{
			this.Pulse(node, m_HidingPulse);
			this.EndSpatialScroll(this); // Free the spatial scroll data owned by this object
		}
	}
}
#endif