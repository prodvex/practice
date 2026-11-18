package com.gamingclub.client

import android.app.Activity
import android.os.Bundle
import android.graphics.Color
import android.view.Gravity
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import org.json.JSONArray
import java.net.HttpURLConnection
import java.net.URL

/**
 * First Android prototype. Loads actual seats from GamingClub.API;
 * does not implement authentication or booking yet.
 */
class MainActivity : Activity() {
    private lateinit var status: TextView
    private lateinit var list: LinearLayout

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(24, 24, 24, 24)
            setBackgroundColor(Color.rgb(17, 24, 39))
        }
        val title = TextView(this).apply {
            text = "GamingClub · Зал"
            textSize = 24f
            setTextColor(Color.WHITE)
        }
        root.addView(title)
        status = TextView(this).apply {
            text = "Нажмите «Обновить» для загрузки мест"
            setTextColor(Color.LTGRAY)
        }
        root.addView(status)
        val refresh = Button(this).apply {
            text = "Обновить"
            setOnClickListener { loadSeats() }
        }
        root.addView(refresh)
        val scroll = ScrollView(this)
        list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        scroll.addView(list)
        root.addView(scroll)
        setContentView(root)
        loadSeats()
    }

    private fun loadSeats() {
        status.text = "Загрузка из API..."
        Thread {
            try {
                val connection = URL("${BuildConfig.API_BASE_URL}/api/seats").openConnection() as HttpURLConnection
                connection.connectTimeout = 5000
                connection.readTimeout = 5000
                try {
                    if (connection.responseCode !in 200..299) error("HTTP ${connection.responseCode}")
                    val payload = JSONArray(connection.inputStream.bufferedReader().use { it.readText() })
                    val seats = mutableListOf<String>()
                    for (i in 0 until payload.length()) {
                        val item = payload.getJSONObject(i)
                        val id = item.getInt("seatNumber")
                        val active = item.getBoolean("isActive")
                        seats.add("ПК №$id  ·  ${if (active) "Доступен" else "Отключён"}")
                    }
                    runOnUiThread {
                        list.removeAllViews()
                        for (label in seats) {
                            list.addView(TextView(this).apply {
                                text = label
                                textSize = 18f
                                setTextColor(Color.WHITE)
                                setPadding(12, 18, 12, 18)
                                gravity = Gravity.CENTER_VERTICAL
                            })
                        }
                        status.text = "Загружено мест: ${seats.size}"
                    }
                } finally { connection.disconnect() }
            } catch (e: Exception) {
                runOnUiThread { status.text = "Нет соединения с API (${e.javaClass.simpleName})" }
            }
        }.start()
    }
}
