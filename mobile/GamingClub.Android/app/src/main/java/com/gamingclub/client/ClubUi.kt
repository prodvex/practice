package com.gamingclub.client

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.TextView

/** Shared Android design tokens matching the six editable GamingClub Figma frames. */
object ClubUi {
    val background: Int = Color.rgb(13, 23, 40)      // #0D1728
    val surface: Int = Color.rgb(27, 41, 66)         // #1B2942
    val raised: Int = Color.rgb(38, 59, 85)          // #263B55
    val primary: Int = Color.rgb(33, 185, 142)       // #21B98E
    val text: Int = Color.rgb(238, 242, 255)         // #EEF2FF
    val muted: Int = Color.rgb(170, 190, 216)        // #AABED8
    val ink: Int = Color.rgb(6, 26, 24)             // #061A18

    fun dp(context: Context, value: Int): Int =
        (context.resources.displayMetrics.density * value + 0.5f).toInt()

    private fun drawable(context: Context, fill: Int, radius: Int): GradientDrawable =
        GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            setColor(fill)
            cornerRadius = dp(context, radius).toFloat()
        }

    fun card(view: View, fill: Int = surface, radius: Int = 14) {
        view.background = drawable(view.context, fill, radius)
        val px = dp(view.context, 14)
        view.setPadding(px, px, px, px)
    }

    fun label(view: TextView, heading: Boolean = false, subtle: Boolean = false) {
        view.setTextColor(if (subtle) muted else text)
        view.textSize = if (heading) 25f else 16f
        if (heading) view.setTypeface(null, android.graphics.Typeface.BOLD)
        view.setPadding(dp(view.context, 2), dp(view.context, 8),
            dp(view.context, 2), dp(view.context, 8))
    }

    fun input(view: EditText) {
        view.setTextColor(text)
        view.setHintTextColor(muted)
        view.textSize = 16f
        view.setSingleLine(true)
        view.background = drawable(view.context, surface, 12)
        view.setPadding(dp(view.context, 15), dp(view.context, 14),
            dp(view.context, 15), dp(view.context, 14))
    }

    fun button(view: Button, primaryAction: Boolean = true) {
        view.setAllCaps(false)
        view.textSize = 14f
        view.gravity = Gravity.CENTER
        view.minimumHeight = 0
        view.minimumWidth = 0
        view.minHeight = 0
        view.minWidth = 0
        view.setTextColor(if (primaryAction) ink else text)
        view.background = drawable(view.context, if (primaryAction) primary else raised, 12)
        view.setPadding(dp(view.context, 12), dp(view.context, 11),
            dp(view.context, 12), dp(view.context, 11))
    }

    fun navigation(view: Button, selected: Boolean) {
        view.setAllCaps(false)
        view.textSize = 11f
        view.gravity = Gravity.CENTER
        view.minimumHeight = 0
        view.minimumWidth = 0
        view.minHeight = 0
        view.minWidth = 0
        view.setTextColor(if (selected) ink else text)
        view.background = drawable(view.context, if (selected) primary else surface, 10)
        view.setPadding(dp(view.context, 2), dp(view.context, 5),
            dp(view.context, 2), dp(view.context, 5))
    }
}
