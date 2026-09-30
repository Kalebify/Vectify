# Frontend — React + TypeScript + Vite, servido como estático vía nginx.
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json* ./
# El bundle web no usa los módulos nativos de las pruebas (canvas). Evitar
# sus scripts de instalación permite construir en Alpine sin toolchain/Cairo.
RUN npm ci --ignore-scripts

COPY . .

# Vite incrusta las variables VITE_* en el bundle en tiempo de build, por eso
# se recibe como build arg (no hay servidor Node en runtime que la lea).
ARG VITE_API_BASE_URL=http://localhost:5080
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
