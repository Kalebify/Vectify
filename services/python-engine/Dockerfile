# Motor de vectorización — Python + FastAPI.
FROM python:3.12-slim

WORKDIR /app

# libgomp1: dependencia de runtime de opencv-python-headless (OpenMP) para el
# pipeline de preprocesamiento (M1-S03). opencv-python-headless no necesita
# libGL/libSM (a diferencia de opencv-python) porque no incluye highgui.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

ENV HOST=0.0.0.0 \
    PORT=8000 \
    LOG_LEVEL=info \
    SERVICE_NAME=vectify-python-engine \
    SERVICE_VERSION=0.1.0

EXPOSE 8000

# host/puerto configurables por variables de entorno, no hardcodeados.
CMD ["sh", "-c", "uvicorn app.main:app --host ${HOST} --port ${PORT}"]
