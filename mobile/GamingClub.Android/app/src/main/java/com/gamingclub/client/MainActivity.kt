package com.gamingclub.client

import android.app.Activity
import android.graphics.Color
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.*
import java.time.Instant
import java.time.temporal.ChronoUnit

/** Development-only Android client backed by GamingClub API. */
class MainActivity : Activity() {
    private val api by lazy { GamingClubApi(BuildConfig.API_BASE_URL) }
    private lateinit var status: TextView
    private lateinit var seatsView: LinearLayout
    private lateinit var hallPlan: HallPlanView
    private lateinit var sessionView: TextView
    private lateinit var tariffsView: Spinner
    private lateinit var userId: EditText
    private lateinit var startUtc: EditText
    private lateinit var endUtc: EditText
    private val tariffs = mutableListOf<Int>()
    private var seatId: Int? = null
    private var bookingId: Int? = null
    private var bookedEnd: Instant? = null
    private val timerHandler = Handler(Looper.getMainLooper())
    private var clockPolling = false
    private var loadingClock = false
    private val clockPoll = object : Runnable {
        override fun run() {
            if (!clockPolling || isFinishing || isDestroyed) return
            val currentBooking = bookingId
            if (currentBooking != null && !loadingClock) {
                loadingClock = true
                execute({ api.clock(currentBooking) }) { value ->
                    loadingClock = false
                    if (clockPolling && bookingId == currentBooking) {
                        val remaining = value.getInt("remainingSeconds")
                        sessionView.text = "Сессия №${value.getInt("sessionId")}, осталось $remaining сек."
                        if (remaining <= 0) stopClockPolling()
                    }
                }
            }
            if (clockPolling) timerHandler.postDelayed(this, 5000)
        }
    }

    private fun startClockPolling() {
        if (clockPolling) return
        clockPolling = true
        timerHandler.post(clockPoll)
    }

    private fun stopClockPolling() {
        clockPolling = false
        timerHandler.removeCallbacks(clockPoll)
    }

    override fun onStop() {
        stopClockPolling()
        super.onStop()
    }

    override fun onStart() {
        super.onStart()
        if (::sessionView.isInitialized && bookingId != null) startClockPolling()
    }

    private fun label(value: String) = TextView(this).apply {
        text = value
        textSize = 18f
        setTextColor(Color.WHITE)
        setPadding(4, 12, 4, 12)
    }
    private fun input(value: String) = EditText(this).apply {
        setText(value)
        setTextColor(Color.WHITE)
        setSingleLine()
    }
    private fun <T> execute(task: () -> T, complete: (T) -> Unit) {
        Thread {
            try {
                val value = task()
                runOnUiThread { complete(value) }
            } catch (ex: Exception) {
                runOnUiThread { status.text = "Ошибка API: ${ex.message}" }
            }
        }.start()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!BuildConfig.DEBUG) {
            setContentView(label("Тестовый клиент доступен только в Debug."))
            return
        }
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.rgb(18, 25, 40))
            setPadding(24, 24, 24, 24)
        }
        val scroll = ScrollView(this)
        scroll.addView(root)
        setContentView(scroll)
        root.addView(label("GamingClub · Вариант 18"))
        status = label("Подключаемся к серверу...")
        root.addView(status)
        root.addView(label("1. Выбор игрового места"))
        root.addView(Button(this).apply {
            text = "Обновить зал"
            setOnClickListener { refresh() }
        })
        hallPlan = HallPlanView(this).apply {
            onSeatSelected = { id, number ->
                seatId = id
                status.text = "Выбран ПК №$number. Доступность на время брони проверяет сервер."
            }
        }
        root.addView(hallPlan)
        root.addView(label("Зелёный — включён; серый — отключён; жёлтый — выбран. Занятость проверяется при бронировании."))
        seatsView = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(seatsView)
        root.addView(label("2. Бронирование"))
        root.addView(label("ID пользователя из SQL Server"))
        userId = input("1")
        userId.inputType = android.text.InputType.TYPE_CLASS_NUMBER
        root.addView(userId)
        tariffsView = Spinner(this)
        root.addView(tariffsView)
        val start = Instant.now().truncatedTo(ChronoUnit.MINUTES).plus(2, ChronoUnit.MINUTES)
        root.addView(label("Начало UTC (ISO 8601)"))
        startUtc = input(start.toString())
        root.addView(startUtc)
        root.addView(label("Конец UTC (ISO 8601)"))
        endUtc = input(start.plus(1, ChronoUnit.HOURS).toString())
        root.addView(endUtc)
        root.addView(Button(this).apply {
            text = "Забронировать"
            setOnClickListener { book() }
        })
        root.addView(label("3. Управление сессией"))
        sessionView = label("Бронирование ещё не создано")
        root.addView(sessionView)
        root.addView(Button(this).apply {
            text = "Запустить сессию"
            setOnClickListener { sessionAction("start") }
        })
        root.addView(Button(this).apply {
            text = "Обновить таймер"
            setOnClickListener { sessionAction("clock") }
        })
        root.addView(Button(this).apply {
            text = "Продлить на 30 минут"
            setOnClickListener { sessionAction("extend") }
        })
        root.addView(label("Учебный прототип: нет регистрации, оплаты и автоматического обновления таймера."))
        refresh()
    }

    private fun refresh() {
        status.text = "Получение мест и тарифов..."
        execute({ api.seats() to api.tariffs() }) { (seats, prices) ->
            seatsView.removeAllViews()
            seatId = null
            hallPlan.submit(seats)
            tariffs.clear()
            val names = mutableListOf<String>()
            for (i in 0 until prices.length()) {
                val tariff = prices.getJSONObject(i)
                tariffs += tariff.getInt("tariffId")
                names += "${tariff.getString("name")} — ${tariff.getDouble("priceRub")} ₽"
            }
            tariffsView.adapter = ArrayAdapter(this,
                android.R.layout.simple_spinner_dropdown_item, names)
            for (i in 0 until seats.length()) {
                val seat = seats.getJSONObject(i)
                val id = seat.getInt("seatId")
                val number = seat.getInt("seatNumber")
                val active = seat.getBoolean("isActive")
                seatsView.addView(Button(this).apply {
                    text = "ПК №$number" + if (active) " · выбрать" else " · отключён"
                    isEnabled = active
                    setOnClickListener {
                        seatId = id
                        status.text = "Выбран ПК №$number. Свободное время проверит сервер."
                    }
                })
            }
            status.text = "Загружено ${seats.length()} мест и ${prices.length()} тарифов"
        }
    }

    private fun book() {
        val seat = seatId
        val uid = userId.text.toString().toIntOrNull()
        val tariff = tariffs.getOrNull(tariffsView.selectedItemPosition)
        val start = runCatching { Instant.parse(startUtc.text.toString()) }.getOrNull()
        val end = runCatching { Instant.parse(endUtc.text.toString()) }.getOrNull()
        if (seat == null || uid == null || uid <= 0 || tariff == null ||
            start == null || end == null || end <= start ||
            start.truncatedTo(ChronoUnit.MINUTES) != start ||
            end.truncatedTo(ChronoUnit.MINUTES) != end) {
            status.text = "Выберите место, тариф, пользователя и корректное UTC-время."
            return
        }
        execute({ api.book(uid, seat, tariff, start.toString(), end.toString()) }) { result ->
            stopClockPolling()
            bookingId = result.getInt("bookingId")
            bookedEnd = end
            sessionView.text = "Бронь №$bookingId, цена ${result.getDouble("priceRub")} ₽"
            status.text = "Бронь успешно создана"
        }
    }

    private fun sessionAction(action: String) {
        val id = bookingId
        if (id == null) {
            status.text = "Сначала создайте бронирование."
            return
        }
        when (action) {
            "start" -> execute({ api.startSession(id) }) {
                status.text = "Сессия запущена"
                startClockPolling()
            }
            "clock" -> execute({ api.clock(id) }) { value ->
                sessionView.text = "Сессия №${value.getInt("sessionId")}, осталось ${value.getInt("remainingSeconds")} сек."
            }
            "extend" -> {
                val oldEnd = bookedEnd ?: return
                val newEnd = oldEnd.plus(30, ChronoUnit.MINUTES)
                execute({ api.extend(id, newEnd.toString()) }) { result ->
                    bookedEnd = newEnd
                    status.text = "Продлено; доплата ${result.getDouble("extraChargeRub")} ₽"
                    sessionAction("clock")
                }
            }
        }
    }
}
