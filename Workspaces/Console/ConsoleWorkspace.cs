﻿using UnityEngine;
using UnityEngine.VR.Workspaces;

public class ConsoleWorkspace : Workspace
{
	[SerializeField]
	private GameObject m_ConsoleWindowPrefab;

	private Transform m_ConsoleWindow;

	public override void Setup()
	{
		// Initial bounds must be set before the base.Setup() is called
		minBounds = new Vector3(kMinBounds.x, kMinBounds.y, 0.27f);
		m_CustomStartingBounds = minBounds;

		base.Setup();

		preventFrontBackResize = true;

		m_ConsoleWindow = instantiateUI(m_ConsoleWindowPrefab).transform;
		m_ConsoleWindow.SetParent(m_WorkspaceUI.sceneContainer, false);

		var bounds = contentBounds;
		var size = bounds.size;
		size.z = 0.1f;
		bounds.size = size;
		contentBounds = bounds;

		m_ConsoleWindow.localScale = size;
	}

	protected override void OnBoundsChanged()
	{
		m_ConsoleWindow.localScale = contentBounds.size;
	}
}