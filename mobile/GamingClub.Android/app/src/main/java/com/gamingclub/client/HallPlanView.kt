package com.gamingclub.client

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.view.MotionEvent
import android.view.View
import org.json.JSONArray
import kotlin.math.min

/** A simple touch-enabled hall plan using coordinates returned by GamingClub.API. */
class HallPlanView(context: Context) : View(context) {
    private data class Place(val id: Int, val number: Int, val x: Int, val y: Int, val active: Boolean)
    private var places = listOf<Place>()
    private var selectedId: Int? = null
    var onSeatSelected: ((Int, Int) -> Unit)? = null

    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val roomPadding = 58f
    private val seatRadius = 32f
    private var factor = 1f
    private var offsetX = roomPadding
    private var offsetY = roomPadding

    /** Replaces map data without inventing availability: inactive != reserved. */
    fun submit(json: JSONArray) {
        places = (0 until json.length()).map { index ->
            val item = json.getJSONObject(index)
            Place(item.getInt("seatId"), item.getInt("seatNumber"),
                item.getInt("x"), item.getInt("y"), item.getBoolean("isActive"))
        }
        selectedId = null
        invalidate()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val preferredHeight = (320 * resources.displayMetrics.density).toInt()
        setMeasuredDimension(resolveSize(400, widthMeasureSpec),
            resolveSize(preferredHeight, heightMeasureSpec))
    }

    private fun updateScale() {
        if (places.isEmpty()) return
        val maxX = places.maxOf { it.x }.toFloat() + seatRadius
        val maxY = places.maxOf { it.y }.toFloat() + seatRadius
        val scaleX = (width - 2f * roomPadding) / maxX.coerceAtLeast(1f)
        val scaleY = (height - 2f * roomPadding) / maxY.coerceAtLeast(1f)
        factor = min(scaleX, scaleY).coerceAtLeast(0.1f)
        offsetX = roomPadding
        offsetY = roomPadding
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(Color.rgb(28, 39, 61))
        updateScale()
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 3f
        paint.color = Color.rgb(115, 137, 170)
        canvas.drawRoundRect(10f, 10f, width - 10f, height - 10f, 18f, 18f, paint)
        paint.style = Paint.Style.FILL
        paint.textAlign = Paint.Align.CENTER
        paint.textSize = 25f
        for (seat in places) {
            val cx = seat.x * factor + offsetX
            val cy = seat.y * factor + offsetY
            paint.color = when {
                !seat.active -> Color.rgb(105, 114, 128)
                seat.id == selectedId -> Color.rgb(250, 191, 69)
                else -> Color.rgb(51, 178, 136)
            }
            canvas.drawCircle(cx, cy, seatRadius, paint)
            paint.color = Color.BLACK
            canvas.drawText(seat.number.toString(), cx, cy + 8f, paint)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (event.action != MotionEvent.ACTION_UP) return true
        val candidate = places.filter { it.active }.minByOrNull { seat ->
            val dx = seat.x * factor + offsetX - event.x
            val dy = seat.y * factor + offsetY - event.y
            dx * dx + dy * dy
        } ?: return true
        val dx = candidate.x * factor + offsetX - event.x
        val dy = candidate.y * factor + offsetY - event.y
        if (dx * dx + dy * dy <= seatRadius * seatRadius) {
            selectedId = candidate.id
            invalidate()
            performClick()
            onSeatSelected?.invoke(candidate.id, candidate.number)
        }
        return true
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }
}
