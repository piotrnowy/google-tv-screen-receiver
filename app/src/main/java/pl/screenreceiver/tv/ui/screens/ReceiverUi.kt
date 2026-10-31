package pl.screenreceiver.tv.ui.screens

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Border
import androidx.tv.material3.Button
import androidx.tv.material3.ButtonDefaults
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import pl.screenreceiver.tv.ui.theme.*

/** Read-only information is a remote focus target too, so D-pad scrolling cannot skip it. */
@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
internal fun TvInfoCard(
    title: String,
    modifier: Modifier = Modifier,
    detail: String? = null,
    titleColor: Color = ReceiverText,
    content: @Composable ColumnScope.() -> Unit = {},
) {
    var focused by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(12.dp)
    Column(
        modifier.fillMaxWidth()
            .onFocusChanged { focused = it.isFocused }
            .semantics(mergeDescendants = true) {}
            .focusable()
            .background(ReceiverSurface, shape)
            .border(if (focused) 2.dp else 1.dp, if (focused) ReceiverAccent else ReceiverBorder, shape)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(title, style = MaterialTheme.typography.titleLarge, color = titleColor)
        if (detail != null) Text(detail, style = MaterialTheme.typography.bodyLarge, color = ReceiverSecondaryText)
        content()
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
internal fun TvAction(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    selected: Boolean = false,
    enabled: Boolean = true,
    tag: String = label,
) {
    Button(
        onClick = onClick,
        modifier = modifier.heightIn(min = 48.dp).testTag(tag),
        enabled = enabled,
        scale = ButtonDefaults.scale(focusedScale = 1f),
        shape = ButtonDefaults.shape(shape = RoundedCornerShape(8.dp)),
        colors = ButtonDefaults.colors(
            containerColor = if (selected) ReceiverPrimary else ReceiverSurface,
            contentColor = ReceiverText,
            focusedContainerColor = ReceiverPrimary,
            focusedContentColor = Color.White,
            pressedContainerColor = Color(0xFF084DBB),
            pressedContentColor = Color.White,
        ),
        border = ButtonDefaults.border(
            border = Border(BorderStroke(1.dp, if (selected) ReceiverPrimary else ReceiverBorder)),
            focusedBorder = Border(BorderStroke(2.dp, Color.White)),
        ),
        contentPadding = PaddingValues(horizontal = 16.dp, vertical = 12.dp),
    ) { Text(label, style = MaterialTheme.typography.labelLarge) }
}
